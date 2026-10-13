using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1441 / #1442: a content-pack file was decrypted by reading it whole and then
/// decrypting into a second whole copy, so a pack video cost twice its size in memory on every
/// play. Decrypt and encrypt stream now. These pin the two things that must not move: the bytes
/// on disk (packs installed by an older build must still open) and the memory bill.
/// </summary>
public class PackEncryptionStreamingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-pack-enc-" + Guid.NewGuid().ToString("N"));

    public PackEncryptionStreamingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static byte[] Noise(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1048576)]
    [InlineData(1048576 + 3)]
    [InlineData(3 * 1048576 + 5)]
    public void AFileEncryptedTheOldWay_StillDecrypts(int length)
    {
        var plain = Noise(length, length + 1);
        File.WriteAllBytes(PathOf("old.enc"), PackEncryptionService.Encrypt(plain));

        PackEncryptionService.DecryptFileToFile(PathOf("old.enc"), PathOf("out.bin"));
        Assert.True(plain.AsSpan().SequenceEqual(File.ReadAllBytes(PathOf("out.bin"))));

        using var stream = PackEncryptionService.DecryptFileToStream(PathOf("old.enc"));
        Assert.Equal(0, stream.Position);
        Assert.True(plain.AsSpan().SequenceEqual(stream.ToArray()));
        Assert.True(plain.AsSpan().SequenceEqual(PackEncryptionService.DecryptFile(PathOf("old.enc"))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(2 * 1048576 + 7)]
    public void AFileEncryptedByTheStream_ReadsBackTheOldWayToo(int length)
    {
        var plain = Noise(length, length + 7);
        File.WriteAllBytes(PathOf("plain.bin"), plain);

        PackEncryptionService.EncryptFile(PathOf("plain.bin"), PathOf("new.enc"));

        var onDisk = File.ReadAllBytes(PathOf("new.enc"));
        Assert.Equal(PackEncryptionService.Encrypt(plain).Length, onDisk.Length);   // same layout: IV + padded cipher
        Assert.True(plain.AsSpan().SequenceEqual(PackEncryptionService.Decrypt(onDisk)));
    }

    [Fact]
    public void AVideoSizedFile_IsNeverHeldInMemory()
    {
        const int size = 48 * 1048576;
        File.WriteAllBytes(PathOf("big.bin"), Noise(size, 99));
        PackEncryptionService.EncryptFile(PathOf("big.bin"), PathOf("big.enc"));   // also warms the key

        long before = GC.GetAllocatedBytesForCurrentThread();
        PackEncryptionService.DecryptFileToFile(PathOf("big.enc"), PathOf("big.out"));
        long decryptBill = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        PackEncryptionService.EncryptFile(PathOf("big.bin"), PathOf("big2.enc"));
        long encryptBill = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(size, new FileInfo(PathOf("big.out")).Length);
        // The old path allocated at least 2x the file to decrypt and 3x to encrypt.
        Assert.True(decryptBill < size / 4, $"decrypt allocated {decryptBill / 1048576} MB for a {size / 1048576} MB file");
        Assert.True(encryptBill < size / 4, $"encrypt allocated {encryptBill / 1048576} MB for a {size / 1048576} MB file");
    }

    [Fact]
    public void ABrokenFile_LeavesNoHalfWrittenCopyBehind()
    {
        var cipher = PackEncryptionService.Encrypt(Noise(4096, 5));
        File.WriteAllBytes(PathOf("cut.enc"), cipher.Take(cipher.Length - 9).ToArray());   // not a whole block

        Assert.ThrowsAny<Exception>(() => PackEncryptionService.DecryptFileToFile(PathOf("cut.enc"), PathOf("cut.out")));
        Assert.False(File.Exists(PathOf("cut.out")));
    }
}
