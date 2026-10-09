using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PdnFormatAddin;

/// <summary>
/// A class instance read from an NRBF stream: its type name and member values.
/// </summary>
sealed class NrbfObject (string typeName, Dictionary<string, object?> members)
{
	public string TypeName { get; } = typeName;
	public Dictionary<string, object?> Members { get; } = members;
	public object? this[string name] => Members[name];
}

/// <summary>
/// Minimal reader for the .NET Remoting Binary Format (MS-NRBF), the format written by
/// BinaryFormatter, which is no longer available in .NET 8+. Only the record types
/// that appear in Paint.NET files are supported.
/// Primitive arrays are returned as object?[], other objects as NrbfObject.
/// </summary>
sealed class NrbfReader
{
	sealed record ClassInfo (string Name, string[] MemberNames, byte[] BinaryTypes, object?[] AdditionalInfo);
	sealed record Reference (int Id);
	sealed record Nulls (int Count);

	readonly BinaryReader reader;
	readonly Dictionary<int, object?> objects = [];
	readonly Dictionary<int, ClassInfo> classes = [];

	NrbfReader (BinaryReader reader)
	{
		this.reader = reader;
	}

	/// <summary>
	/// Reads records up to and including MessageEnd, leaving the stream right after it.
	/// Returns the root object.
	/// </summary>
	public static object? Read (BinaryReader reader)
	{
		NrbfReader nrbf = new (reader);
		int rootId = 0;

		while (true) {
			byte type = reader.ReadByte ();
			if (type == 11) // MessageEnd
				break;

			if (type == 0) { // SerializedStreamHeader
				rootId = reader.ReadInt32 ();
				reader.ReadBytes (12); // header id, major and minor version
			} else {
				nrbf.ReadRecord (type);
			}
		}

		nrbf.ResolveReferences ();
		return nrbf.objects[rootId];
	}

	object? ReadRecord () => ReadRecord (reader.ReadByte ());

	object? ReadRecord (byte type)
	{
		switch (type) {
		case 1: { // ClassWithId: reuses the layout of an earlier class
			int id = reader.ReadInt32 ();
			return ReadClassValues (id, classes[reader.ReadInt32 ()]);
		}
		case 4: // SystemClassWithMembersAndTypes
		case 5: { // ClassWithMembersAndTypes
			int id = reader.ReadInt32 ();
			string name = reader.ReadString ();
			string[] memberNames = new string[reader.ReadInt32 ()];
			for (int i = 0; i < memberNames.Length; i++)
				memberNames[i] = reader.ReadString ();
			byte[] binaryTypes = reader.ReadBytes (memberNames.Length);
			object?[] additionalInfo = binaryTypes.Select (ReadAdditionalInfo).ToArray ();
			if (type == 5)
				reader.ReadInt32 (); // library id

			ClassInfo info = new (name, memberNames, binaryTypes, additionalInfo);
			classes[id] = info;
			return ReadClassValues (id, info);
		}
		case 6: { // BinaryObjectString
			int id = reader.ReadInt32 ();
			return objects[id] = reader.ReadString ();
		}
		case 7: { // BinaryArray
			int id = reader.ReadInt32 ();
			byte arrayType = reader.ReadByte ();
			int rank = reader.ReadInt32 ();
			int length = 1;
			for (int i = 0; i < rank; i++)
				length *= reader.ReadInt32 ();
			if (arrayType >= 3) // the "Offset" array types carry lower bounds
				reader.ReadBytes (4 * rank);
			byte binaryType = reader.ReadByte ();
			object? info = ReadAdditionalInfo (binaryType);
			return objects[id] = binaryType == 0 ? ReadPrimitives ((byte) info!, length) : ReadObjects (length);
		}
		case 8: // MemberPrimitiveTyped
			return ReadPrimitive (reader.ReadByte ());
		case 9: // MemberReference
			return new Reference (reader.ReadInt32 ());
		case 10: // ObjectNull
			return null;
		case 12: // BinaryLibrary: precedes the record that uses it
			reader.ReadInt32 ();
			reader.ReadString ();
			return ReadRecord ();
		case 13: // ObjectNullMultiple256
			return new Nulls (reader.ReadByte ());
		case 14: // ObjectNullMultiple
			return new Nulls (reader.ReadInt32 ());
		case 15: { // ArraySinglePrimitive
			int id = reader.ReadInt32 ();
			int length = reader.ReadInt32 ();
			return objects[id] = ReadPrimitives (reader.ReadByte (), length);
		}
		case 16: // ArraySingleObject
		case 17: { // ArraySingleString
			int id = reader.ReadInt32 ();
			return objects[id] = ReadObjects (reader.ReadInt32 ());
		}
		default:
			throw new InvalidDataException ($"Unsupported NRBF record type {type}");
		}
	}

	NrbfObject ReadClassValues (int id, ClassInfo info)
	{
		NrbfObject obj = new (info.Name, new Dictionary<string, object?> (info.MemberNames.Length));
		objects[id] = obj;

		for (int i = 0; i < info.MemberNames.Length; i++) {
			obj.Members[info.MemberNames[i]] = info.BinaryTypes[i] == 0
				? ReadPrimitive ((byte) info.AdditionalInfo[i]!)
				: ReadRecord ();
		}

		return obj;
	}

	object? ReadAdditionalInfo (byte binaryType)
	{
		switch (binaryType) {
		case 0: // Primitive
		case 7: // PrimitiveArray
			return reader.ReadByte ();
		case 3: // SystemClass
			return reader.ReadString ();
		case 4: { // Class
			string name = reader.ReadString ();
			reader.ReadInt32 (); // library id
			return name;
		}
		default:
			return null;
		}
	}

	object?[] ReadObjects (int length)
	{
		object?[] items = new object?[length];
		for (int i = 0; i < length;) {
			object? value = ReadRecord ();
			if (value is Nulls nulls)
				i += nulls.Count;
			else
				items[i++] = value;
		}
		return items;
	}

	object?[] ReadPrimitives (byte primitiveType, int length)
	{
		object?[] items = new object?[length];
		for (int i = 0; i < length; i++)
			items[i] = ReadPrimitive (primitiveType);
		return items;
	}

	object ReadPrimitive (byte primitiveType) => primitiveType switch {
		1 => reader.ReadBoolean (),
		2 => reader.ReadByte (),
		3 => reader.ReadChar (),
		5 or 18 => reader.ReadString (), // Decimal is stored as a string
		6 => reader.ReadDouble (),
		7 => reader.ReadInt16 (),
		8 => reader.ReadInt32 (),
		9 or 12 or 13 => reader.ReadInt64 (), // TimeSpan and DateTime are 64-bit ticks
		10 => reader.ReadSByte (),
		11 => reader.ReadSingle (),
		14 => reader.ReadUInt16 (),
		15 => reader.ReadUInt32 (),
		16 => reader.ReadUInt64 (),
		_ => throw new InvalidDataException ($"Unsupported NRBF primitive type {primitiveType}"),
	};

	/// Replaces MemberReference placeholders with the objects they point to.
	void ResolveReferences ()
	{
		foreach (object? value in objects.Values) {
			if (value is NrbfObject obj) {
				foreach (string key in obj.Members.Keys.ToList ())
					obj.Members[key] = Resolve (obj.Members[key]);
			} else if (value is object?[] items) {
				for (int i = 0; i < items.Length; i++)
					items[i] = Resolve (items[i]);
			}
		}
	}

	object? Resolve (object? value) => value is Reference r ? objects[r.Id] : value;
}
