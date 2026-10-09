using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PdnFormatAddin;

enum PdnBlendMode { Normal, Multiply, Additive, ColorBurn, ColorDodge, Reflect, Glow, Overlay, Difference, Negation, Lighten, Darken, Screen, Xor }

/// <param name="Pixels">BGRA pixels with straight (non-premultiplied) alpha.</param>
sealed record PdnLayer (string Name, bool Visible, byte Opacity, PdnBlendMode BlendMode, byte[] Pixels);

/// <param name="Layers">Bottom layer first.</param>
sealed record PdnImage (int Width, int Height, List<PdnLayer> Layers);

/// <summary>
/// Reads Paint.NET 3.x - 5.x images. File layout: "PDN3", 24-bit header length, XML header,
/// 0x00 0x01, the document serialized with BinaryFormatter (NRBF), then the pixels of each layer.
/// See https://github.com/addisonElliott/pypdn for reference.
/// </summary>
static class PdnReader
{
	public static PdnImage Read (Stream stream)
	{
		using BinaryReader reader = new (stream, Encoding.UTF8, leaveOpen: true);

		if (Encoding.ASCII.GetString (reader.ReadBytes (4)) != "PDN3")
			throw new InvalidDataException ("Not a Paint.NET image");
		byte[] headerSize = reader.ReadBytes (3);
		reader.ReadBytes (headerSize[0] | headerSize[1] << 8 | headerSize[2] << 16); // XML header, not needed
		if (reader.ReadByte () != 0 || reader.ReadByte () != 1)
			throw new InvalidDataException ("Unsupported Paint.NET image");

		NrbfObject document = (NrbfObject) NrbfReader.Read (reader)!;
		int width = (int) document["width"]!;
		int height = (int) document["height"]!;

		// The layer list derives from ArrayList, whose item array may be padded with nulls
		NrbfObject layerList = (NrbfObject) document["layers"]!;
		object?[] items = (object?[]) layerList["ArrayList+_items"]!;
		int count = (int) layerList["ArrayList+_size"]!;

		List<PdnLayer> layers = [];
		for (int i = 0; i < count; i++) {
			NrbfObject layer = (NrbfObject) items[i]!;
			NrbfObject properties = (NrbfObject) layer["Layer+properties"]!;
			NrbfObject surface = (NrbfObject) layer["surface"]!;
			NrbfObject memoryBlock = (NrbfObject) surface["scan0"]!;

			if ((int) surface["width"]! != width || (int) surface["height"]! != height || (int) surface["stride"]! != width * 4)
				throw new InvalidDataException ("Unsupported layer format");

			layers.Add (new PdnLayer (
				Name: properties["name"] as string ?? "",
				Visible: (bool) properties["visible"]!,
				Opacity: (byte) properties["opacity"]!,
				BlendMode: GetBlendMode (layer, properties),
				// Pixel data follows the NRBF document, in layer order
				Pixels: ReadPixels (reader, (long) memoryBlock["length64"]!)));
		}

		return new PdnImage (width, height, layers);
	}

	/// Paint.NET 4+ stores a blend mode enum, older versions a blend op object.
	static PdnBlendMode GetBlendMode (NrbfObject layer, NrbfObject properties)
	{
		if (properties.Members.TryGetValue ("blendMode", out object? mode) && mode is NrbfObject modeEnum)
			return (PdnBlendMode) (int) modeEnum["value__"]!;

		if (layer.Members.TryGetValue ("properties", out object? bitmapProps)
			&& bitmapProps is NrbfObject props
			&& props.Members.TryGetValue ("blendOp", out object? op)
			&& op is NrbfObject blendOp) {
			// e.g. "PaintDotNet.UserBlendOps+MultiplyBlendOp"
			string name = blendOp.TypeName[(blendOp.TypeName.LastIndexOf ('+') + 1)..].Replace ("BlendOp", "");
			if (Enum.TryParse (name, out PdnBlendMode parsed))
				return parsed;
		}

		return PdnBlendMode.Normal;
	}

	/// Pixels are split into chunks (in any order), each optionally gzip-compressed.
	/// See MemoryBlock.cs in OpenPDN.
	static byte[] ReadPixels (BinaryReader reader, long length)
	{
		byte[] data = new byte[length];
		bool compressed = reader.ReadByte () == 0;
		int chunkSize = ReadInt32BigEndian (reader);
		long chunkCount = (length + chunkSize - 1) / chunkSize;

		for (long i = 0; i < chunkCount; i++) {
			int chunkIndex = ReadInt32BigEndian (reader);
			int dataSize = ReadInt32BigEndian (reader);
			long offset = (long) chunkIndex * chunkSize;
			Span<byte> target = data.AsSpan ((int) offset, (int) Math.Min (chunkSize, length - offset));
			byte[] chunk = reader.ReadBytes (dataSize);

			if (compressed) {
				using GZipStream gzip = new (new MemoryStream (chunk), CompressionMode.Decompress);
				gzip.ReadExactly (target);
			} else {
				chunk.CopyTo (target);
			}
		}

		return data;
	}

	static int ReadInt32BigEndian (BinaryReader reader)
		=> BinaryPrimitives.ReadInt32BigEndian (reader.ReadBytes (4));
}
