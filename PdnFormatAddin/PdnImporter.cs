using System;
using System.IO;
using System.Runtime.InteropServices;
using Pinta.Core;

namespace PdnFormatAddin;

public sealed class PdnImporter : IImageImporter
{
	public Document Import (Gio.File file)
	{
		using GioStream stream = new (file.Read (cancellable: null));
		using BufferedStream bufferedStream = new (stream);
		PdnImage image = PdnReader.Read (bufferedStream);

		Document document = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			new Size (image.Width, image.Height),
			file,
			"pdn");

		foreach (PdnLayer pdnLayer in image.Layers) {
			UserLayer layer = document.Layers.CreateLayer (pdnLayer.Name);
			document.Layers.Insert (layer, document.Layers.Count ()); // on top of the previous one

			layer.Hidden = !pdnLayer.Visible;
			layer.Opacity = pdnLayer.Opacity / 255.0;
			layer.BlendMode = ToPintaBlendMode (pdnLayer.BlendMode);

			// Paint.NET uses straight alpha, Cairo premultiplied alpha
			ReadOnlySpan<ColorBgra> source = MemoryMarshal.Cast<byte, ColorBgra> (pdnLayer.Pixels);
			layer.Surface.Flush ();
			Span<ColorBgra> target = layer.Surface.GetPixelData ();
			for (int i = 0; i < target.Length; i++)
				target[i] = source[i].ToPremultipliedAlpha ();
			layer.Surface.MarkDirty ();
		}

		return document;
	}

	// Additive, Reflect, Glow and Negation have no equivalent in Pinta
	static BlendMode ToPintaBlendMode (PdnBlendMode mode) => mode switch {
		PdnBlendMode.Multiply => BlendMode.Multiply,
		PdnBlendMode.ColorBurn => BlendMode.ColorBurn,
		PdnBlendMode.ColorDodge => BlendMode.ColorDodge,
		PdnBlendMode.Overlay => BlendMode.Overlay,
		PdnBlendMode.Difference => BlendMode.Difference,
		PdnBlendMode.Lighten => BlendMode.Lighten,
		PdnBlendMode.Darken => BlendMode.Darken,
		PdnBlendMode.Screen => BlendMode.Screen,
		PdnBlendMode.Xor => BlendMode.Xor,
		_ => BlendMode.Normal,
	};
}
