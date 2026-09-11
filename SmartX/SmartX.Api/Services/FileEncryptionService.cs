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

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async Task<Stream> DecryptFromFileAsync(string sourcePath, CancellationToken ct = default)
        {

        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//