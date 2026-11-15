/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Google.Protobuf;

[EditorBrowsable(EditorBrowsableState.Never)]
internal static class YaeCodedInputStreamExtensions
{
    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern byte[] ReadRawBytes(CodedInputStream stream, int size);

    public static CodedInputStream ReadLengthDelimitedAsStream(this CodedInputStream stream)
        => new(ReadRawBytes(stream, stream.ReadLength()));
}