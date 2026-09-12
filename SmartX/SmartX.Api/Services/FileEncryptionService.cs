using System.Security.Cryptography;

namespace SmartX.Api.Services
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class FileEncryptionService
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);



        /// <summary>
        /// 
        /// </summary>
        public async Task<long> EncryptToFileAsync(Stream source, string destinationPath, CancellationToken ct = default)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();

            await using var destination = File.Create(destinationPath);
            await destination.WriteAsync(aes.IV, ct);

            await using var cryptoStream = new CryptoStream(destination, aes.CreateEncryptor(), CryptoStreamMode.Write);
            await source.CopyToAsync(cryptoStream, ct);
            await cryptoStream.FlushFinalBlockAsync(ct);

            return new FileInfo(destinationPath).Length;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<Stream> DecryptFromFileAsync(string sourcePath, CancellationToken ct = default)
        {
            var source = File.OpenRead(sourcePath);

            var iv = new byte[16];
            await source.ReadExactlyAsync(iv, ct);

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = iv;

            var output = new MemoryStream();

            await using (var cryptoStream = new CryptoStream(source, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true))
            {
                await cryptoStream.CopyToAsync(output, ct);
            }

            source.Dispose();
            output.Position = 0;
            return output;
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//