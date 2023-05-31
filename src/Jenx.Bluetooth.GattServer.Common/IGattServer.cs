using System;
using System.Threading.Tasks;
using static Jenx.Bluetooth.GattServer.Common.GattServer;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Jenx.Bluetooth.GattServer.Common
{
    public interface IGattServer
    {
        event GattCharacteristicHandler OnCharacteristicWrite;
        Task Initialize();
        Task<bool> AddReadCharacteristicAsync(Guid characteristicId, byte[] characteristicValue, string userDescription);
        Task<GattLocalCharacteristic> AddReadIndicateCharacteristicAsync(Guid characteristicId, byte[] characteristicValue, string userDescription);
        Task<bool> AddWriteCharacteristicAsync(Guid characteristicId, string userDescription);
        Task<bool> AddReadWriteCharacteristicAsync(Guid characteristicId, string userDescription);
        Task<GattLocalCharacteristic> AddNotifyCharacteristicAsync(Guid characteristicId, string userDescription);
        void Start();
        void Stop();
    }
}