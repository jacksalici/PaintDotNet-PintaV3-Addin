using Pinta.Core;

namespace PdnFormatAddin;

[Mono.Addins.Extension]
public class PdnFormatExtension : IExtension
{
	public void Initialize ()
	{
		FormatDescriptor descriptor = new (
			displayPrefix: "Paint.NET",
			extensions: ["pdn", "PDN"],
			mimes: ["image/x-paintnet"],
			importer: new PdnImporter (),
			exporter: null,
			supportsLayers: true);
		PintaCore.ImageFormats.RegisterFormat (descriptor);
	}

	public void Uninitialize ()
	{
		PintaCore.ImageFormats.UnregisterFormatByExtension ("pdn");
	}
}
