﻿using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Jenx.Bluetooth.GattServer.Common
{
    public class GattServer : IGattServer
    {
        private GattServiceProvider _gattServiceProvider;
        private readonly ILogger _logger;
        private readonly Guid _serviceId;

        public delegate void GattCharacteristicHandler(object myObject, CharacteristicEventArgs myArgs);

        public event GattCharacteristicHandler OnCharacteristicWrite;

        public GattServer(Guid serviceId, ILogger logger)
        {
            _logger = logger;
            _serviceId = serviceId;
        }

        public async Task Initialize()
        {
            var cellaGatService = await GattServiceProvider.CreateAsync(_serviceId);

            if (cellaGatService.Error == BluetoothError.RadioNotAvailable)
            {
                throw new Exception("BLE not enabled");
            };

            if (cellaGatService.Error == BluetoothError.Success)
            {
                _gattServiceProvider = cellaGatService.ServiceProvider;
            }

            _gattServiceProvider.AdvertisementStatusChanged += async (sender, args) =>
            {
                await _logger.LogMessageAsync(
                    sender.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Started ?
                    "GATT server started." :
                    "GATT server stopped.");
            };
        }

        public async Task<bool> AddReadCharacteristicAsync(Guid characteristicId, byte[] characteristicValue, string userDescription = "N/A")
        {
            await _logger.LogMessageAsync($"Adding read characteristic to gatt service: description: {userDescription}, guid: {characteristicId}, value: {characteristicValue}.");

            var charactericticParameters = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read,
                StaticValue = characteristicValue.AsBuffer(),
                ReadProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = userDescription
            };

            var characteristicResult = await _gattServiceProvider.Service.CreateCharacteristicAsync(characteristicId, charactericticParameters);

            var readCharacteristic = characteristicResult.Characteristic;
            readCharacteristic.ReadRequested += async (a, b) =>
            {
                await _logger.LogMessageAsync("read requested..");
            }; // Warning, dont remove this...

            return characteristicResult.Error == BluetoothError.Success;
        }
        public async Task<GattLocalCharacteristic> AddReadIndicateCharacteristicAsync(Guid characteristicId, byte[] characteristicValue, string userDescription = "N/A")
        {
            await _logger.LogMessageAsync($"Adding read characteristic to gatt service: description: {userDescription}, guid: {characteristicId}, value: {characteristicValue}.");

            var charactericticParameters = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Indicate,
                StaticValue = characteristicValue.AsBuffer(),
                ReadProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = userDescription
            };

            var characteristicResult = await _gattServiceProvider.Service.CreateCharacteristicAsync(characteristicId, charactericticParameters);

            var readCharacteristic = characteristicResult.Characteristic;
            readCharacteristic.ReadRequested += async (a, b) =>
            {
                await _logger.LogMessageAsync("read requested..");
            }; // Warning, dont remove this...

            return readCharacteristic;
        }

        public async Task<bool> AddWriteCharacteristicAsync(Guid characteristicId, string userDescription = "N/A")
        {
            await _logger.LogMessageAsync($"Adding write characteristic to service: description: {userDescription}, guid: {characteristicId}");

            var charactericticParameters = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Write,
                WriteProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = userDescription
            };

            var characteristicResult = await _gattServiceProvider.Service.CreateCharacteristicAsync(characteristicId, charactericticParameters);
            if (characteristicResult.Error != BluetoothError.Success)
            {
                await _logger.LogMessageAsync("Adding write characteristic failed");
                return false;
            }

            var characteristic = characteristicResult.Characteristic;
            characteristic.WriteRequested += async (sender, args) =>
            {
                using (args.GetDeferral())
                {
                    // For this you need UWP bluetooth manifest enabled on app !!!!!!!!
                    var request = await args.GetRequestAsync();
                    if (request == null)
                    {
                        return;
                    }

                    using (var dataReader = DataReader.FromBuffer(request.Value))
                    {
                        var characteristicValue = dataReader.ReadString(request.Value.Length);

                        if (OnCharacteristicWrite != null)
                            OnCharacteristicWrite(null, new CharacteristicEventArgs(characteristic.Uuid, characteristicValue));
                    }

                    if (request.Option == GattWriteOption.WriteWithResponse)
                    {
                        request.Respond();
                    }
                }
            };
            return true;
        }

        public async Task<bool> AddReadWriteCharacteristicAsync(Guid characteristicId, string userDescription = "N/A")
        {
            await _logger.LogMessageAsync($"Adding write characteristic to cella service: description: {userDescription}, guid: {characteristicId}");

            var charactericticParameters = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse | GattCharacteristicProperties.Read,
                WriteProtectionLevel = GattProtectionLevel.Plain,
                ReadProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = userDescription
            };

            var characteristicResult = await _gattServiceProvider.Service.CreateCharacteristicAsync(characteristicId, charactericticParameters);
            if (characteristicResult.Error != BluetoothError.Success)
            {
                await _logger.LogMessageAsync("Adding write characteristic failed");
                return false;
            }

            var characteristic = characteristicResult.Characteristic;
            characteristic.WriteRequested += async (sender, args) =>
            {
                using (args.GetDeferral())
                {
                    // For this you need UWP bluetooth manifest enabled on app !!!!!!!!
                    var request = await args.GetRequestAsync();
                    if (request == null)
                    {
                        return;
                    }

                    using (var dataReader = DataReader.FromBuffer(request.Value))
                    {
                        var characteristicValue = dataReader.ReadString(request.Value.Length);

                        if (OnCharacteristicWrite != null)
                            OnCharacteristicWrite(this, new CharacteristicEventArgs(characteristic.Uuid, characteristicValue));
                    }

                    if (request.Option == GattWriteOption.WriteWithResponse)
                    {
                        request.Respond();
                    }
                }
            };

            characteristic.ReadRequested += async (sender, args) =>
            {
                var deferral = args.GetDeferral();
                var request = await args.GetRequestAsync();
                var writer = new DataWriter();
                request.RespondWithValue(writer.DetachBuffer());
                deferral.Complete();
            };

            return true;
        }

        public async Task<GattLocalCharacteristic> AddNotifyCharacteristicAsync(Guid characteristicId, string userDescription = "N/A")
        {
            await _logger.LogMessageAsync($"Adding notify characteristic to gatt service: description: {userDescription}, guid: {characteristicId}.");

            var charactericticParameters = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Notify,
                ReadProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = userDescription
            };

            //var characteristicResult = await _gattServiceProvider.Service.CreateCharacteristicAsync(characteristicId, charactericticParameters);
            //return characteristicResult.Error == BluetoothError.Success;
            // Create the heart rate characteristic for the service

            GattLocalCharacteristicResult result =
                await _gattServiceProvider.Service.CreateCharacteristicAsync(
                    characteristicId, charactericticParameters);

            if (result.Error != BluetoothError.Success) {
                await _logger.LogMessageAsync($"CreateCharacteristicAsync()->{result.Error}");
                return null;
            }
            // Grab the characterist object from the service set it to the HeartRate property which is of a specfic Characteristic type
            return result.Characteristic;
        }

        public void Start()
        {
            if (_gattServiceProvider.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Created ||
                _gattServiceProvider.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Stopped)
            {
                var advParameters = new GattServiceProviderAdvertisingParameters
                {
                    IsDiscoverable = true,
                    IsConnectable = true
                };
                _gattServiceProvider.StartAdvertising(advParameters);
            }
        }

        public void Stop()
        {
            _gattServiceProvider.StopAdvertising(); // Warning: This does not change AdvertisementStatus status, fails on second click...
        }
    }
}