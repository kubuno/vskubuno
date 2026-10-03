using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Kubuno.Shared.DevAssistant.Logic.Credentials
{
    /// <summary>
    /// Generic credentials of the Windows Credential Manager (docs/AI-ASSISTANT.md section 8.1), persisted with
    /// <c>CRED_PERSIST_LOCAL_MACHINE</c> like <c>kubuno-desktop-secrets</c> so they do not roam with a roaming profile. The VSIX
    /// only writes, checks and deletes keys; the host process alone reads one, when it builds a provider: a key never
    /// crosses the stdio channel, an environment variable, a file or a log.
    /// </summary>
    public static class WindowsCredentialStore
    {
        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;

        /// <summary>Stores <paramref name="secret"/> under <paramref name="target"/> (replacing any previous value).</summary>
        public static void Write(string target, string userName, string secret)
        {
            var blob = Encoding.Unicode.GetBytes(secret ?? string.Empty);
            var blobPointer = Marshal.AllocCoTaskMem(Math.Max(blob.Length, 1));
            try
            {
                Marshal.Copy(blob, 0, blobPointer, blob.Length);
                var credential = new NativeCredential
                {
                    Type = CredTypeGeneric,
                    TargetName = target,
                    UserName = userName,
                    CredentialBlobSize = blob.Length,
                    CredentialBlob = blobPointer,
                    Persist = CredPersistLocalMachine,
                };
                if (!CredWrite(ref credential, 0))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                // Overwrite the unmanaged copy before freeing it.
                for (int i = 0; i < blob.Length; i++)
                {
                    Marshal.WriteByte(blobPointer, i, 0);
                }

                Array.Clear(blob, 0, blob.Length);
                Marshal.FreeCoTaskMem(blobPointer);
            }
        }

        /// <summary>The secret stored under <paramref name="target"/>, or null when there is none.</summary>
        public static string? Read(string target)
        {
            if (!CredRead(target, CredTypeGeneric, 0, out var pointer))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound)
                {
                    return null;
                }

                throw new Win32Exception(error);
            }

            try
            {
                var credential = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential))!;
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                {
                    return string.Empty;
                }

                return Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        /// <summary>Whether a credential exists under <paramref name="target"/> (without returning its value).</summary>
        public static bool Exists(string target)
        {
            if (!CredRead(target, CredTypeGeneric, 0, out var pointer))
            {
                return false;
            }

            CredFree(pointer);
            return true;
        }

        /// <summary>Deletes the credential; false when there was none.</summary>
        public static bool Delete(string target)
        {
            if (CredDelete(target, CredTypeGeneric, 0))
            {
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return false;
            }

            throw new Win32Exception(error);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll", SetLastError = false)]
        private static extern void CredFree(IntPtr buffer);
    }
}
