using System;
using System.Threading.Tasks;

namespace WS_Setup_6.Core.Interfaces
{
    public interface IBaselineService
    {
        void DecryptConfig(
            string inFile,
            string outFile,
            byte[] key,
            byte[] iv);

        Task RunDscSimpleAsync(string yamlPath);
    }
}