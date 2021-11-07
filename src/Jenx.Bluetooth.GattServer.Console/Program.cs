using Jenx.Bluetooth.GattServer.Common;
using System.Linq;
using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.ComponentModel;
using Windows.Storage.Streams;
using System.Diagnostics;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;

namespace Jenx.Bluetooth.GattServer.Console
{
    /// <summary>
    /// Base class for any characteristic. This handles basic responds for read/write and supplies method to 
    /// notify or indicate clients. 
    /// </summary>
    public class GenericGattCharacteristic : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged requirements
        /// <summary>
        /// Property changed event
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Property changed method
        /// </summary>
        /// <param name="e">Property that changed</param>
        protected void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, e);
            }
        }
        #endregion

        /// <summary>
        /// Source of <see cref="Characteristic"/> 
        /// </summary>
        private GattLocalCharacteristic characteristic;

        /// <summary>
        /// Gets or sets <see cref="characteristic"/>  that is wrapped by this class
        /// </summary>
        public GattLocalCharacteristic Characteristic
        {
            get
            {
                return characteristic;
            }

            set
            {
                if (characteristic != value)
                {
                    characteristic = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Characteristic"));
                }
            }
        }

        /// <summary>
        /// Source of <see cref="Value"/> 
        /// </summary>
        private IBuffer value;

        /// <summary>
        /// Gets or sets the Value of the characteristic
        /// </summary>
        public IBuffer Value
        {
            get
            {
                return value;
            }

            set
            {
                if (this.value != value)
                {
                    this.value = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Value"));
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GenericGattCharacteristic" /> class.
        /// </summary>
        /// <param name="characteristic">Characteristic this wraps</param>
        public GenericGattCharacteristic(GattLocalCharacteristic characteristic)
        {
            Characteristic = characteristic;
            Characteristic.SubscribedClientsChanged += Characteristic_SubscribedClientsChanged;
        }

        /// <summary>
        /// Base implementation when number of subscribers changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected virtual void Characteristic_SubscribedClientsChanged(GattLocalCharacteristic sender, object args)
        {
            Debug.WriteLine($"{sender.UserDescription} Subscribers: {sender.SubscribedClients.Count()}");
        }

        /// <summary>
        /// Base implementation to Notify or Indicate clients 
        /// </summary>
        public virtual async void NotifyValue()
        {
            bool notify = Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify);
            bool indicate = Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate);

            if (notify || indicate)
            {
                //Debug.WriteLine($"NotifyValue executing: Notify = {notify}, Indicate = {indicate}");
                await Characteristic.NotifyValueAsync(Value);
            } else
            {
                Debug.WriteLine("NotifyValue was called but CharacteristicProperties don't include Notify or Indicate");
            }
        }
    }
    public class CharacteristicConvertor
    {
        private GenericGattCharacteristic _power, _cadence;

        public CharacteristicConvertor(GattLocalCharacteristic power, GattLocalCharacteristic cad)
        {
            _power = new GenericGattCharacteristic(power);
            _cadence = new GenericGattCharacteristic(cad);
        }

        public void SchwinnConnectionStatusChangeHandler(BluetoothLEDevice bluetoothLEDevice, Object o)
        {
            Debug.WriteLine($"The Schwinn is now: {bluetoothLEDevice.ConnectionStatus}");
        }

        public long mLastCalories = 0;
        public int mLastTime = 0; //в 1024-х долях секунды
        public double mLastPower = -1.0;
        public long mLastRxTime = 0;

        //cadence calculator
        public long[] mCadTimes = new long[600]; //currentTimeMillis
        public int[] mCadRotations = new int[600];
        public int mCadPointer = -1;
        public byte mLastCadLowbyte = 0;
        private void updateCadence(int cadRotations)
        {
            mCadPointer++;
            int cadPointer = mCadPointer % 600;
            mCadTimes[cadPointer] = mLastRxTime;
            if (mCadTimes[cadPointer] == 0) mCadTimes[cadPointer] = 1;
            mCadRotations[cadPointer] = cadRotations;
        }
        public long mLastCalcCadTime = 0;
        public int mLastCalcCad = 0;
        public int currentCadence()
        {
            long t = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            if (t - mLastCalcCadTime < 1000 && mLastCalcCad != 0)
                return mLastCalcCad;
            if (t - mLastRxTime > 5000)
                return 0;
            int cadPointer = mCadPointer;
            int maxRot = mCadRotations[cadPointer % 600];
            int minRot = maxRot;
            long timeDuration = 0;
            while (timeDuration < 60000)
            {
                cadPointer--;
                if (cadPointer < 0) cadPointer += 600;
                long tryTime = mCadTimes[cadPointer % 600];
                if (tryTime == 0 || t - tryTime > 61000) break;
                timeDuration = t - tryTime;
                minRot = mCadRotations[cadPointer % 600];
                if (maxRot < minRot) maxRot += 0x1000000;
                if (timeDuration > 15000 && maxRot - minRot > 20) break;
            }
            if (timeDuration == 0) mLastCalcCad = 0;
            else mLastCalcCad = (int)((60000.0 / timeDuration) * (maxRot - minRot) + 0.5);
            mLastCalcCadTime = t;
            return mLastCalcCad;
        }
        public void SchwinnChangeHandler(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(eventArgs.CharacteristicValue, out data);
            if (data.Length == 17 && data[0] == 17 && data[1] == 32 && data[2] == 0)
            {
                mLastRxTime = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                updateCadence((data[4] & 0xFF) | ((data[5] & 0xFF) << 8) | ((data[6] & 0xFF) << 16));
                long calories = data[10] | (data[11] << 8) | (data[12] << 16) | (data[13] << 24) | (data[14] << 32) | ((data[15] & 0x7F) << 40);
                int tim = data[8] | (data[9] << 8);
                if (mLastCalories == 0 || tim == mLastTime)
                {
                    mLastCalories = calories;
                    mLastTime = tim;
                } else
                {
                    long dcalories = calories - mLastCalories;
                    mLastCalories = calories;
                    int dtime = tim - mLastTime;
                    mLastTime = tim;
                    if (dtime < 0) dtime += 65536;

                    double mult = 0.42;
                    double power = (double)dcalories / (double)dtime * mult;
                    if (mLastPower == -1.0 || Math.Abs(mLastPower - power) < 100.0)
                        mLastPower = power;
                    else
                        mLastPower += (power - mLastPower) / 2.0;
                    if (mLastPower < 0)
                        mLastPower = 0;
                    Debug.WriteLine($"Time: {(int)(tim / 1024)}s, {mLastPower}W, {calories >> 8}c, {currentCadence()}rpm");
                }
            } else return;

            if (_power.Characteristic.SubscribedClients.Count() > 0)
            {
                // flags
                // 00000001 - 1   - 0x001 - Pedal Power Balance Present
                // 00000010 - 2   - 0x002 - Pedal Power Balance Reference
                // 00000100 - 4   - 0x004 - Accumulated Torque Present
                // 00001000 - 8   - 0x008 - Accumulated Torque Source
                // 00010000 - 16  - 0x010 - Wheel Revolution Data Present
                // 00100000 - 32  - 0x020 - Crank Revolution Data Present
                // 01000000 - 64  - 0x040 - Extreme Force Magnitudes Present
                // 10000000 - 128 - 0x080 - Extreme Torque Magnitudes Present
                int pwr = (int)mLastPower;
                byte[] value = { 0, 0, //flags
                    (byte)(pwr & 0xFF), (byte)((pwr >> 8) & 0xFF), //power
                };
                _power.Value = ToIBuffer(value);
                _power.NotifyValue();
            }
            if (_cadence.Characteristic.SubscribedClients.Count() > 0 && mLastCadLowbyte != data[4])
            {
                mLastCadLowbyte = data[4];
                byte[] value = { 2,   //flags
                    data[4], data[5], //crank revolution #
                    data[8], data[9]  //time
                };
                _cadence.Value = ToIBuffer(value);
                _cadence.NotifyValue();
            }
        }

        public static IBuffer ToIBuffer(byte[] data)
        {
            DataWriter writer = new DataWriter();
            writer.ByteOrder = ByteOrder.LittleEndian;
            writer.WriteBytes(data);
            return writer.DetachBuffer();
        }
    }

    internal class Program
    {
        const string SCHWINN_MAC_ADDRESS = "84:71:27:27:4A:44";
        const string SCHWINN_BLUETOOTH_LE_SERVICE_UUID = "3bf58980-3a2f-11e6-9011-0002a5d5c51b";
        const string SCHWINN_BLUETOOTH_LE_CHARACTERISTIC_UUID = "5c7d82a0-9803-11e3-8a6c-0002a5d5c51b";

        static private ILogger _logger;
        static private IGattServer _gattServerPwr, _gattServerCad;
        static private CharacteristicConvertor _convertor;

        private static async Task Main(string[] args)
        {
            InitializeLogger();
            InitializeGattServer();
            await StartGattServer();
            await StartGattClient();
            await StartLooping();
        }

        #region Private
        private static void InitializeLogger()
        {
            _logger = new ConsoleLogger();
        }

        private static void InitializeGattServer()
        {
            _gattServerPwr = new Common.GattServer(GattServiceUuids.CyclingPower, _logger);
            _gattServerCad = new Common.GattServer(GattServiceUuids.CyclingSpeedAndCadence, _logger);
        }

        private static async Task StartGattServer()
        {
            try
            {
                await _logger.LogMessageAsync("Starting Initializong Jenx.si Bluetooth Gatt service.");
                await _gattServerPwr.Initialize(); await _gattServerCad.Initialize();
                await _logger.LogMessageAsync("Jenx.si Bluetooth Gatt service initialized.");
            }
            catch
            {
                await _logger.LogMessageAsync("Error starting Jenx.si Bluetooth Gatt service.");
                throw;
            }
            var myPwrCharacteristic = await _gattServerPwr.AddNotifyCharacteristicAsync(GattCharacteristicUuids.CyclingPowerMeasurement, "Power & Cadence Measurement");
            await _gattServerPwr.AddReadCharacteristicAsync(GattCharacteristicUuids.CyclingPowerFeature, new byte[] { 0x00, 0x00, 1, 0 }, "CyclingPowerFeature");
            await _gattServerPwr.AddReadCharacteristicAsync(GattCharacteristicUuids.SensorLocation, new byte[] { 12 /* rear wheel */ }, "SensorLocation");

            var myCadCharacteristic = await _gattServerCad.AddNotifyCharacteristicAsync(GattCharacteristicUuids.CscMeasurement, "CscMeasurement");
            await _gattServerCad.AddReadCharacteristicAsync(GattCharacteristicUuids.CscFeature, new byte[] { 0x2, 0x00 }, "CscFeature");
            await _gattServerCad.AddReadCharacteristicAsync(GattCharacteristicUuids.SensorLocation, new byte[] { 12 /* rear wheel */ }, "SensorLocation");

            if (myPwrCharacteristic == null || myCadCharacteristic == null)
                return;

            _convertor = new CharacteristicConvertor(myPwrCharacteristic, myCadCharacteristic);

            _gattServerPwr.Start(); _gattServerCad.Start();
            await _logger.LogMessageAsync("Jenx.si Bluetooth Gatt service started.");
        }

        private static async Task StartGattClient()
        {
            BluetoothLEDevice bluetoothLEDevice = null;

            //loop through bluetooth devices until we found our desired device by mac address
            DeviceInformationCollection deviceInformationCollection = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelector());
            foreach (var deviceInformation in deviceInformationCollection)
            {
                String deviceInformationId = deviceInformation.Id;
                String mac = deviceInformationId.Substring(deviceInformationId.Length - 17);
                if (mac.ToUpper().Equals(SCHWINN_MAC_ADDRESS))
                {
                    bluetoothLEDevice = await BluetoothLEDevice.FromIdAsync(deviceInformation.Id);
                    await _logger.LogMessageAsync(string.Format($"Found Bluetooth LE Device [{mac}]: {bluetoothLEDevice.ConnectionStatus}"));
                    break;
                }
            }

            //Subscribe to the connection status change event
            bluetoothLEDevice.ConnectionStatusChanged += _convertor.SchwinnConnectionStatusChangeHandler;

            //get the desired service
            Guid serviceGuid = Guid.Parse(SCHWINN_BLUETOOTH_LE_SERVICE_UUID);
            GattDeviceServicesResult serviceResult = await bluetoothLEDevice.GetGattServicesForUuidAsync(serviceGuid);
            if (serviceResult.Status == GattCommunicationStatus.Success)
            {
                GattDeviceService service = serviceResult.Services[0];
                Debug.WriteLine($"Service @ {service.Uuid}, found and accessed!");

                //get the desired characteristic
                Guid characteristicGuid = Guid.Parse(SCHWINN_BLUETOOTH_LE_CHARACTERISTIC_UUID);
                GattCharacteristicsResult characteristicResult = await service.GetCharacteristicsForUuidAsync(characteristicGuid);
                if (characteristicResult.Status == GattCommunicationStatus.Success)
                {
                    GattCharacteristic characteristic = characteristicResult.Characteristics[0];
                    Debug.WriteLine($"Characteristic @ {characteristic.Uuid} found and accessed!");

                    //check access to the characteristic
                    Debug.Write("We have the following access to the characteristic: ");
                    GattCharacteristicProperties properties = characteristic.CharacteristicProperties;
                    foreach (GattCharacteristicProperties property in Enum.GetValues(typeof(GattCharacteristicProperties)))
                    {
                        if (properties.HasFlag(property))
                        {
                            Debug.Write($"{property} ");
                        }
                    }
                    Debug.WriteLine("");

                    //subscribe to the GATT characteristic's notification
                    GattCommunicationStatus status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    if (status == GattCommunicationStatus.Success)
                    {
                        Debug.WriteLine("Subscribing to the Indication/Notification");
                        characteristic.ValueChanged += _convertor.SchwinnChangeHandler;
                    }
                    else
                    {
                        Debug.WriteLine($"ERR1: {status}");
                    }
                } else
                {
                    Debug.WriteLine($"ERR2: {characteristicResult.Status}");
                }
            } else
            {
                Debug.WriteLine($"ERR3: {serviceResult.Status}");
            }
        }

        private static async Task StartLooping()
        {
            System.ConsoleKeyInfo cki;
            System.Console.CancelKeyPress += new System.ConsoleCancelEventHandler(KeyPressHandler);

            while (true)
            {
                await _logger.LogMessageAsync("Press any key, or 'X' to quit, or ");
                await _logger.LogMessageAsync("CTRL+C to interrupt the read operation:");
                cki = System.Console.ReadKey(true);
                await _logger.LogMessageAsync($"  Key pressed: {cki.Key}\n");

                // Exit if the user pressed the 'X' key.
                if (cki.Key == System.ConsoleKey.X) break;
            }
        }

        private static async void KeyPressHandler(object sender, System.ConsoleCancelEventArgs args)
        {
            await _logger.LogMessageAsync("\nThe read operation has been interrupted.");
            await _logger.LogMessageAsync($"  Key pressed: {args.SpecialKey}");
            await _logger.LogMessageAsync($"  Cancel property: {args.Cancel}");
            await _logger.LogMessageAsync("Setting the Cancel property to true...");
            args.Cancel = true;

            await _logger.LogMessageAsync($"  Cancel property: {args.Cancel}");
            await _logger.LogMessageAsync("The read operation will resume...\n");
        }

        private static void StopGattServer()
        {
            _gattServerPwr.Stop(); _gattServerCad.Stop();
        }

        #endregion Private
    }
}