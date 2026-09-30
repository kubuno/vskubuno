using System;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.Exceptions;
using Microsoft.VisualStudio.Debugger.Native;

namespace Kubuno.VisualStudio.Debugger
{
    /// <summary>
    /// <see cref="IDkmExceptionFormatter"/> for the C++ exception category (the filter in
    /// Kubuno.VisualStudio.Debugger.vsdconfigxml): a <c>rust_panic</c> - the C++ exception every Rust
    /// panic raises on MSVC - is described as "Rust panic: &lt;message&gt;", the message read from the
    /// panic payload (<see cref="RustPanicPayload"/>), like the Message of a .NET exception in the exception
    /// helper; every other exception goes to the next formatter unchanged.
    /// </summary>
    public sealed class RustPanicExceptionFormatter : IDkmExceptionFormatter
    {
        /// <summary>The one-line description: the Output window's "Exception thrown at ..." line and the helper's title.</summary>
        public string GetDescription(DkmExceptionInformation exception)
        {
            var message = TryReadMessage(exception);
            return message == null ? exception.GetDescription() : RustPanicPayload.Describe(message);
        }

        /// <summary>The text shown under the title when Visual Studio stops on the exception.</summary>
        public string GetAdditionalInformation(DkmExceptionInformation exception)
        {
            var message = TryReadMessage(exception);
            // The next formatter may have nothing to add (null): passed through as is.
            return message == null ? exception.GetAdditionalInformation()! : RustPanicPayload.Explain(message);
        }

        private static string? TryReadMessage(DkmExceptionInformation exception)
        {
            if (exception is not DkmCppExceptionInformation cpp
                || !RustPanicPayload.IsRustPanicName(cpp.Name))
            {
                return null;
            }

            var process = cpp.Process;
            try
            {
                return RustPanicPayload.ReadMessage(cpp.ExceptionObjectPointer, (address, count) => Read(process, address, count));
            }
            catch (Exception)
            {
                // A formatter must never fail the exception event: fall back to the default text.
                return null;
            }
        }

        private static byte[]? Read(DkmProcess process, ulong address, int count)
        {
            try
            {
                var buffer = new byte[count];
                var read = process.ReadMemory(address, DkmReadMemoryFlags.None, buffer);
                if (read <= 0)
                {
                    return null;
                }

                if (read < count)
                {
                    Array.Resize(ref buffer, read);
                }

                return buffer;
            }
            catch (DkmException)
            {
                return null;
            }
        }
    }
}
