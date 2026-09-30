using System;
using System.Text;

namespace Kubuno.VisualStudio.Debugger
{
    /// <summary>
    /// Reads the message of a Rust panic out of the debuggee, starting from the object the MSVC
    /// <c>rust_panic</c> C++ exception carries (<c>panic_unwind::imp::Exception</c>, see the toolchain's
    /// <c>library/panic_unwind/src/seh.rs</c>):
    /// <code>
    /// struct Exception {
    ///     canary: *const _TypeDescriptor,           // points at the TypeDescriptor named "rust_panic"
    ///     data: Option&lt;Box&lt;dyn Any + Send&gt;&gt;,    // the panic payload: (data, vtable) or (0, _)
    /// }
    /// </code>
    /// The payload of <c>panic!("literal")</c>/<c>unwrap()</c> is a <c>&amp;'static str</c>, the one of a
    /// formatted <c>panic!("{x}")</c> a <c>String</c>; anything else (<c>panic_any</c>) has no text.
    ///
    /// Nothing here relies on the compiler's field order, which Rust does not guarantee: the canary is
    /// found by what it points at, a <c>str</c>/<c>String</c> is told apart by the size in the trait
    /// object's vtable (<c>[drop_in_place, size, align, ...]</c>), and a <c>String</c>'s pointer by being
    /// the largest of its three words (a heap address is always above a length or a capacity). Any read
    /// that fails or any shape that does not match gives <see langword="null"/>, never an exception.
    /// Pure logic over a memory reader, so it is unit-tested without a debuggee.
    /// </summary>
    public static class RustPanicPayload
    {
        /// <summary>The C++ type name of the exception Rust raises for a panic on MSVC (its TypeDescriptor's name).</summary>
        public const string ExceptionTypeName = "rust_panic";

        /// <summary>
        /// The name Visual Studio gives that exception (Output window, exception helper, Exception Settings): it
        /// undecorates the TypeDescriptor name as if it were an MSVC one (<c>.?AVclass@@</c>, first character
        /// skipped, <c>UNDNAME_TYPE_ONLY</c>), which turns <c>rust_panic</c> into <c>" ?? ::st_panic"</c> - checked with
        /// dbghelp's <c>UnDecorateSymbolName("ust_panic", UNDNAME_32_BIT_DECODE | UNDNAME_TYPE_ONLY)</c> and in the
        /// Output window ("Microsoft C++ exception:  ?? ::st_panic at memory location ...").
        /// </summary>
        public const string VisualStudioExceptionName = " ?? ::st_panic";

        /// <summary>Whether a C++ exception name (as the debugger reports it) is a Rust panic.</summary>
        public static bool IsRustPanicName(string? name) =>
            name != null && (name == ExceptionTypeName || name.Trim() == VisualStudioExceptionName.Trim());

        /// <summary>The longest message returned; a longer one is cut and ends with an ellipsis.</summary>
        public const int MaxMessageBytes = 4096;

        /// <summary>
        /// Reads <paramref name="count"/> bytes at <paramref name="address"/> of the debuggee, or returns
        /// <see langword="null"/> when that memory cannot be read.
        /// </summary>
        public delegate byte[]? MemoryReader(ulong address, int count);

        /// <summary>
        /// The panic message of the <c>rust_panic</c> exception object at <paramref name="exceptionObject"/>,
        /// <c>"Box&lt;dyn Any&gt;"</c> for a payload that is not text (what Rust's own hook prints), or
        /// <see langword="null"/> when the object does not look like a Rust panic.
        /// </summary>
        public static string? ReadMessage(ulong exceptionObject, MemoryReader read)
        {
            if (exceptionObject == 0 || read == null)
            {
                return null;
            }

            var words = ReadWords(read, exceptionObject, 3);
            if (words == null)
            {
                return null;
            }

            // The canary is either before or after the 16-byte fat pointer.
            ulong dataPointer, vtable;
            if (IsRustPanicTypeDescriptor(read, words[0]))
            {
                (dataPointer, vtable) = (words[1], words[2]);
            }
            else if (IsRustPanicTypeDescriptor(read, words[2]))
            {
                (dataPointer, vtable) = (words[0], words[1]);
            }
            else
            {
                return null;
            }

            if (dataPointer == 0 || vtable == 0)
            {
                // `None`: a copy of the exception whose payload was already taken.
                return null;
            }

            var vtableHead = ReadWords(read, vtable, 3);
            if (vtableHead == null)
            {
                return null;
            }

            var size = vtableHead[1];
            switch (size)
            {
                case 16:
                    // &'static str: (ptr, len) in some order.
                    var str = ReadWords(read, dataPointer, 2);
                    return str == null ? null : ReadText(read, Math.Max(str[0], str[1]), Math.Min(str[0], str[1])) ?? BoxDynAny;
                case 24:
                    // String = Vec<u8>: ptr, capacity and length in some order, length <= capacity.
                    var s = ReadWords(read, dataPointer, 3);
                    if (s == null)
                    {
                        return null;
                    }

                    Array.Sort(s);
                    return ReadText(read, s[2], s[0]) ?? BoxDynAny;
                default:
                    return BoxDynAny;
            }
        }

        /// <summary>What Rust's default hook prints for a payload that is neither a <c>&amp;str</c> nor a <c>String</c>.</summary>
        public const string BoxDynAny = "Box<dyn Any>";

        /// <summary>The exception helper/Output window text for a panic message.</summary>
        public static string Describe(string message) => "Rust panic: " + message;

        /// <summary>The exception helper's body for a panic message: the message, then what continuing does.</summary>
        public static string Explain(string message) =>
            message + "\r\n\r\nThe code panicked on this line. Continue (F5) to let the panic unwind (a Kubuno application then " +
            "shows its crash window); Exception Settings > C++ Exceptions > \"" + VisualStudioExceptionName.Trim() + "\" turns this break off.";

        private static bool IsRustPanicTypeDescriptor(MemoryReader read, ulong address)
        {
            // struct _TypeDescriptor { pVFTable: *const u8, spare: *mut u8, name: [u8; 11] } -> name at +16.
            if (address == 0)
            {
                return false;
            }

            var name = read(address + 16, ExceptionTypeName.Length + 1);
            if (name == null || name.Length < ExceptionTypeName.Length + 1)
            {
                return false;
            }

            return Encoding.ASCII.GetString(name, 0, ExceptionTypeName.Length) == ExceptionTypeName && name[ExceptionTypeName.Length] == 0;
        }

        private static string? ReadText(MemoryReader read, ulong pointer, ulong length)
        {
            if (length == 0)
            {
                return string.Empty;
            }

            if (pointer == 0 || length > int.MaxValue)
            {
                return null;
            }

            var cut = length > MaxMessageBytes;
            var bytes = read(pointer, (int)Math.Min(length, MaxMessageBytes));
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(bytes);
            return cut ? text + "…" : text;
        }

        private static ulong[]? ReadWords(MemoryReader read, ulong address, int count)
        {
            var bytes = read(address, count * 8);
            if (bytes == null || bytes.Length < count * 8)
            {
                return null;
            }

            var words = new ulong[count];
            for (var i = 0; i < count; i++)
            {
                words[i] = BitConverter.ToUInt64(bytes, i * 8);
            }

            return words;
        }
    }
}
