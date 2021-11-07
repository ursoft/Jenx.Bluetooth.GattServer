using Jenx.Bluetooth.GattServer.Common;
using System.Linq;
using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.ComponentModel;
using Windows.Storage.Streams;
using System.Diagnostics;
using System.Threading;

namespace Jenx.Bluetooth.GattServer.Console
{
    /// <summary>
    /// Base class for any characteristic. This handles basic responds for read/write and supplies method to 
    /// notify or indicate clients. 
    /// </summary>
    public abstract class GenericGattCharacteristic : INotifyPropertyChanged
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

            if (Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
            {
                Characteristic.ReadRequested += Characteristic_ReadRequested;
            }

            if (Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||
                Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            {
                Characteristic.WriteRequested += Characteristic_WriteRequested;
            }

            Characteristic.SubscribedClientsChanged += Characteristic_SubscribedClientsChanged;
        }

        /// <summary>
        /// Base implementation when number of subscribers changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected virtual void Characteristic_SubscribedClientsChanged(GattLocalCharacteristic sender, object args)
        {
            Debug.WriteLine("Subscribers: {0}", sender.SubscribedClients.Count());
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

        /// <summary>
        /// Base implementation for the read callback
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void Characteristic_ReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
        {
            // Grab the event deferral before performing any async operations in the handler.
            var deferral = args.GetDeferral();

            Debug.WriteLine($"({this.GetType()})Entering base.Characteristic_ReadRequested");

            // In order to get the remote request, access to the device must be provided by the user.
            // This can be accomplished by calling BluetoothLEDevice.RequestAccessAsync(), or by getting the request on the UX thread.
            //
            // Note that subsequent calls to RequestAccessAsync or GetRequestAsync for the same device do not need to be called on the UX thread.
            /*await CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(
                async () =>
                {
                    var request = await args.GetRequestAsync();

                    Debug.WriteLine($"Characteristic_ReadRequested - Length {request.Length}, State: {request.State}, Offset: {request.Offset}");

                    if (!ReadRequested(args.Session, request))
                    {
                        request.RespondWithValue(Value);
                    }

                    deferral.Complete();
                });*/
        }

        protected virtual bool ReadRequested(GattSession session, GattReadRequest request)
        {
            Debug.WriteLine("Request not completed by derrived class.");
            return false;
        }

        /// <summary>
        /// Base implementation for the write callback
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void Characteristic_WriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
        {
            Debug.WriteLine("Characteristic_WriteRequested: Write Requested");

            // Grab the event deferral before performing any async operations in the handler.
            var deferral = args.GetDeferral();

            // In order to get the remote request, access to the device must be provided by the user.
            // This can be accomplished by calling BluetoothLEDevice.RequestAccessAsync(), or by getting the request on the UX thread.
            //
            // Note that subsequent calls to RequestAccessAsync or GetRequestAsync for the same device do not need to be called on the UX thread.
            /*
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(
                async () =>
                {
                    // Grab the request
                    var request = await args.GetRequestAsync();

                    Debug.WriteLine($"Characteristic_WriteRequested - Length {request.Value.Length}, State: {request.State}, Offset: {request.Offset}");

                    if (!WriteRequested(args.Session, request))
                    {
                        // Set the characteristic Value
                        Value = request.Value;

                        // Respond with completed
                        if (request.Option == GattWriteOption.WriteWithResponse)
                        {
                            Debug.WriteLine("Characteristic_WriteRequested: Completing request with responds");
                            request.Respond();
                        } else
                        {
                            Debug.WriteLine("Characteristic_WriteRequested: Completing request without responds");
                        }
                    }

                    // everything below this is debug. Should implement this on non-UI thread based on
                    // https://github.com/Microsoft/Windows-task-snippets/blob/master/tasks/UI-thread-task-await-from-background-thread.md
                    byte[] data;
                    CryptographicBuffer.CopyToByteArray(Value, out data);

                    if (data == null)
                    {
                        Debug.WriteLine("Characteristic_WriteRequested: Value after write complete was NULL");
                    } else
                    {
                        Debug.WriteLine($"Characteristic_WriteRequested: New Value: {data.BytesToString()}");
                    }

                    deferral.Complete();
                });*/
        }

        protected virtual bool WriteRequested(GattSession session, GattWriteRequest request)
        {
            Debug.WriteLine("Request not completed by derrived class.");
            return false;
        }
    }
    /// <summary>
    /// Microsoft boilerplate characteristic that supports 'Notify' provided for completeness. This service is almost identical to MicrosoftIndicateCharacteristic.
    /// </summary>
    public class PowerMeasurementCharacteristic : GenericGattCharacteristic
    {
        private Timer heartRateTicker = null;
        private int mTxCounter = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="PowerMeasurementCharacteristic" /> class.
        /// </summary>
        /// <param name="characteristic">Characteristic this wraps</param>
        public PowerMeasurementCharacteristic(GattLocalCharacteristic characteristic) : base(characteristic)
        {
            heartRateTicker = new Timer(UpdateHeartRate, "", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void UpdateHeartRate(Object state)
        {
            mTxCounter++;
            SetHeartRate();
        }

        public static IBuffer ToIBuffer(byte[] data)
        {
            DataWriter writer = new DataWriter();
            writer.ByteOrder = ByteOrder.LittleEndian;
            writer.WriteBytes(data);
            return writer.DetachBuffer();
        }

        private void SetHeartRate()
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

            int time = Environment.TickCount;
            byte[] value = { 32, 0, //flags
                10, 0, //power
                (byte)(mTxCounter & 0xFF), (byte)((mTxCounter >> 8) & 0xFF), //revolution #
                (byte)(time & 0xFF), (byte)((time >> 8) & 0xFF)   //time
            };
      /*event.rev_count = event.rev_count % 65536;
      //debugCSP("rev_count: " + event.rev_count);
      
      buffer.writeUInt16LE(event.rev_count, 4);
  
      var now = Date.now();
      var now_1024 = Math.floor(now*1e3/1024);
      var event_time = now_1024 % 65536; // rolls over every 64 seconds
      debugCSP("event time: " + event_time);
      buffer.writeUInt16LE(event_time, 6);*/

            Value = ToIBuffer(value);
            NotifyValue();
        }

        /// <summary>
        /// Override so we can update the value before notifying or indicating the client
        /// </summary>
        public override void NotifyValue()
        {
            base.NotifyValue();
        }
    }

    internal class Program
    {
        static private ILogger _logger;
        static private IGattServer _gattServer;
        static private PowerMeasurementCharacteristic _myCharacteristic;

        private static async Task Main(string[] args)
        {
            InitializeLogger();
            InitializeGattServer();
            await StartGattServer();
            await StartLooping();
        }

        #region Private

        private static void InitializeLogger()
        {
            _logger = new ConsoleLogger();
        }

        private static void InitializeGattServer()
        {
            _gattServer = new Common.GattServer(GattServiceUuids.CyclingPower, _logger);
            _gattServer.OnCharacteristicWrite += GattServerOnCharacteristicWrite;
        }

        private static async Task StartGattServer()
        {
            try
            {
                await _logger.LogMessageAsync("Starting Initializong Jenx.si Bluetooth Gatt service.");
                await _gattServer.Initialize();
                await _logger.LogMessageAsync("Jenx.si Bluetooth Gatt service initialized.");
            }
            catch
            {
                await _logger.LogMessageAsync("Error starting Jenx.si Bluetooth Gatt service.");
                throw;
            }

            //await _gattServer.AddReadWriteCharacteristicAsync(GattCharacteristicIdentifiers.DataExchange, "Data exchange");
            //await _gattServer.AddReadCharacteristicAsync(GattCharacteristicUuids., "1.0.0.1", "Firmware Version");
            //await _gattServer.AddWriteCharacteristicAsync(GattCharacteristicIdentifiers.InitData, "Init info");
            var myCharacteristic = await _gattServer.AddNotifyCharacteristicAsync(GattCharacteristicUuids.CyclingPowerMeasurement, "Power & Cadence Measurement");

            if (myCharacteristic == null)
                return;

            _myCharacteristic = new PowerMeasurementCharacteristic(myCharacteristic);

            _gattServer.Start();
            await _logger.LogMessageAsync("Jenx.si Bluetooth Gatt service started.");
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

        private static async void GattServerOnCharacteristicWrite(object myObject, CharacteristicEventArgs myArgs)
        {
            await _logger.LogMessageAsync($"Characteristic with Guid: {myArgs.Characteristic.ToString()} changed: {myArgs.Value.ToString()}");
        }

        private static void StopGattServer()
        {
            _gattServer.Stop();
        }

        #endregion Private
    }
}