using System;
using System.Collections.Generic;
using System.Text;
using Kubuno.VisualStudio.Debugger;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Debugging
{
    /// <summary>
    /// <see cref="RustPanicPayload"/> against a fake debuggee memory laid out like rustc 1.98's
    /// <c>panic_unwind::imp::Exception</c> and its <c>Box&lt;dyn Any + Send&gt;</c> payload.
    /// </summary>
    [TestClass]
    public sealed class RustPanicPayloadTests
    {
        private const ulong TypeDescriptor = 0x7FF6_0000_1000;
        private const ulong Exception = 0x0000_00A0_0000_2000;
        private const ulong Box = 0x0000_01C0_0000_3000;
        private const ulong Vtable = 0x7FF6_0000_4000;
        private const ulong Text = 0x0000_01C0_0000_5000;

        private sealed class FakeMemory
        {
            private readonly Dictionary<ulong, byte[]> _blocks = new();

            public void Write(ulong address, byte[] bytes) => _blocks[address] = bytes;

            public void WriteWords(ulong address, params ulong[] words)
            {
                var bytes = new byte[words.Length * 8];
                for (var i = 0; i < words.Length; i++)
                {
                    BitConverter.GetBytes(words[i]).CopyTo(bytes, i * 8);
                }

                Write(address, bytes);
            }

            public byte[]? Read(ulong address, int count)
            {
                foreach (var block in _blocks)
                {
                    if (address >= block.Key && address + (ulong)count <= block.Key + (ulong)block.Value.Length)
                    {
                        var result = new byte[count];
                        Array.Copy(block.Value, (int)(address - block.Key), result, 0, count);
                        return result;
                    }
                }

                return null;
            }
        }

        private static FakeMemory Panic(ulong payloadSize, bool canaryFirst = true)
        {
            var memory = new FakeMemory();
            // _TypeDescriptor { pVFTable, spare, name: "rust_panic\0" }
            var descriptor = new byte[16 + 11];
            Encoding.ASCII.GetBytes("rust_panic").CopyTo(descriptor, 16);
            memory.Write(TypeDescriptor, descriptor);
            if (canaryFirst)
            {
                memory.WriteWords(Exception, TypeDescriptor, Box, Vtable);
            }
            else
            {
                memory.WriteWords(Exception, Box, Vtable, TypeDescriptor);
            }

            // vtable: drop_in_place, size, align
            memory.WriteWords(Vtable, 0x7FF6_0000_9000, payloadSize, 8);
            return memory;
        }

        [TestMethod]
        public void A_formatted_panic_reads_its_String_payload()
        {
            var memory = Panic(24);
            var message = "index out of bounds: the len is 3 but the index is 4";
            memory.Write(Text, Encoding.UTF8.GetBytes(message));
            // String { cap, ptr, len } - field order is not relied on.
            memory.WriteWords(Box, 64, Text, (ulong)message.Length);

            Assert.AreEqual(message, RustPanicPayload.ReadMessage(Exception, memory.Read));
        }

        [TestMethod]
        public void A_literal_panic_reads_its_str_payload_whatever_the_field_order()
        {
            var memory = Panic(16, canaryFirst: false);
            var message = "called `Option::unwrap()` on a `None` value";
            memory.Write(Text, Encoding.UTF8.GetBytes(message));
            memory.WriteWords(Box, (ulong)message.Length, Text);

            Assert.AreEqual(message, RustPanicPayload.ReadMessage(Exception, memory.Read));
        }

        [TestMethod]
        public void Non_ascii_text_is_decoded_as_utf8()
        {
            var memory = Panic(24);
            var bytes = Encoding.UTF8.GetBytes("échec : « données »");
            memory.Write(Text, bytes);
            memory.WriteWords(Box, Text, (ulong)bytes.Length, (ulong)bytes.Length);

            Assert.AreEqual("échec : « données »", RustPanicPayload.ReadMessage(Exception, memory.Read));
        }

        [TestMethod]
        public void A_payload_that_is_not_text_is_box_dyn_any()
        {
            var memory = Panic(4);
            memory.WriteWords(Box, 42);

            Assert.AreEqual(RustPanicPayload.BoxDynAny, RustPanicPayload.ReadMessage(Exception, memory.Read));
        }

        [TestMethod]
        public void An_object_without_the_rust_panic_canary_is_not_a_panic()
        {
            var memory = new FakeMemory();
            memory.WriteWords(Exception, 0x1234, Box, Vtable);
            memory.Write(0x1234 + 16, Encoding.ASCII.GetBytes("std::exception\0"));

            Assert.IsNull(RustPanicPayload.ReadMessage(Exception, memory.Read));
        }

        [TestMethod]
        public void A_taken_payload_and_unreadable_memory_give_no_message()
        {
            var taken = Panic(24);
            taken.WriteWords(Exception, TypeDescriptor, 0, 0);
            Assert.IsNull(RustPanicPayload.ReadMessage(Exception, taken.Read));

            var unreadable = Panic(24);
            unreadable.WriteWords(Box, 64, Text, 10);
            Assert.AreEqual(RustPanicPayload.BoxDynAny, RustPanicPayload.ReadMessage(Exception, unreadable.Read));
            Assert.IsNull(RustPanicPayload.ReadMessage(0, unreadable.Read));
        }

        [TestMethod]
        public void The_panic_is_recognized_by_the_name_visual_studio_gives_it()
        {
            Assert.IsTrue(RustPanicPayload.IsRustPanicName(" ?? ::st_panic"));
            Assert.IsTrue(RustPanicPayload.IsRustPanicName("?? ::st_panic"));
            Assert.IsTrue(RustPanicPayload.IsRustPanicName("rust_panic"));
            Assert.IsFalse(RustPanicPayload.IsRustPanicName("std::exception"));
            Assert.IsFalse(RustPanicPayload.IsRustPanicName(null));
            StringAssert.StartsWith(RustPanicPayload.Explain("boom"), "boom\r\n");
        }

        [TestMethod]
        public void A_huge_message_is_cut()
        {
            var memory = Panic(24);
            var bytes = Encoding.ASCII.GetBytes(new string('x', RustPanicPayload.MaxMessageBytes + 100));
            memory.Write(Text, bytes);
            memory.WriteWords(Box, (ulong)bytes.Length, Text, (ulong)bytes.Length);

            var message = RustPanicPayload.ReadMessage(Exception, memory.Read)!;
            Assert.AreEqual(RustPanicPayload.MaxMessageBytes + 1, message.Length);
            Assert.IsTrue(message.EndsWith("…", StringComparison.Ordinal));
            Assert.AreEqual("Rust panic: boom", RustPanicPayload.Describe("boom"));
        }
    }
}
