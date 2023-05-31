using Jenx.Bluetooth.GattServer.Common;
using System.Linq;
using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.ComponentModel;
using Windows.Storage.Streams;
using System.Diagnostics;
using System.Threading;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace Jenx.Bluetooth.GattServer.Console {
    public abstract class GenericGattCharacteristic : INotifyPropertyChanged {
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
        public GattLocalCharacteristic Characteristic {
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
        protected virtual void Characteristic_SubscribedClientsChanged(GattLocalCharacteristic sender, object args)
        {
            Debug.WriteLine("Subscribers: {0}", sender.SubscribedClients.Count());
        }
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
        private async void Characteristic_ReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
        {
            var deferral = args.GetDeferral();
            Debug.WriteLine($"({this.GetType()})Entering base.Characteristic_ReadRequested");
        }
        protected virtual bool ReadRequested(GattSession session, GattReadRequest request)
        {
            Debug.WriteLine("Request not completed by derrived class.");
            return false;
        }
        private async void Characteristic_WriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
        {
            Debug.WriteLine("Characteristic_WriteRequested: Write Requested");
            var deferral = args.GetDeferral();
        }
        protected virtual bool WriteRequested(GattSession session, GattWriteRequest request)
        {
            Debug.WriteLine("Request not completed by derrived class.");
            return false;
        }
    }
    public class PowerMeasurementCharacteristic : GenericGattCharacteristic
    {
        private Timer ticker = null;
        public PowerMeasurementCharacteristic(GattLocalCharacteristic characteristic) : base(characteristic)
        {
            ticker = new Timer(Update, "", TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
        }
        private void Update(Object state)
        {
            Calc();
        }
        public static IBuffer ToIBuffer(byte[] data)
        {
            DataWriter writer = new DataWriter();
            writer.ByteOrder = ByteOrder.LittleEndian;
            writer.WriteBytes(data);
            return writer.DetachBuffer();
        }
        public static int m_power = 200, m_revs = 0, m_firstDt = Environment.TickCount, m_lastRevDt = 0;
        public static Random rg = new Random(0);
        private void Calc()
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

            int time = Environment.TickCount - m_firstDt;
            int cadDt = 60000 / (80 + (rg.Next() % 20));
            int dt = time - m_lastRevDt;
            if (dt >= cadDt) {
                m_revs++;
                m_lastRevDt = time;
            }

            byte[] value = { 0x20, 0, //flags
                (byte)(m_power & 255), (byte)(m_power/256),
                (byte)(m_revs & 255), (byte)(m_revs>>8), (byte)(m_lastRevDt & 255), (byte)(m_lastRevDt>>8)
            };
            Value = ToIBuffer(value);
            NotifyValue();
        }
        public override void NotifyValue()
        {
            base.NotifyValue();
        }
    }
    public class SteeringCharacteristicAuthTx : GenericGattCharacteristic
    {
        public bool m_auth = false, m_wasCtrl = false;
        public bool Authenticate() {
            if (m_wasCtrl && !m_auth) {
                byte[] value = { 0xff, 0x13, 0xFF };
                Value = ToIBuffer(value);
                NotifyValue();
                m_auth = true;
            }
            return m_wasCtrl;
        }
        public async Task OnControl() {
            m_wasCtrl = true;
            byte[] value = { 0x3, 0x11, 0xFF };
            Value = ToIBuffer(value);
            NotifyValue();
        }
        public SteeringCharacteristicAuthTx(GattLocalCharacteristic characteristic) : base(characteristic)
        {
        }
        public static IBuffer ToIBuffer(byte[] data)
        {
            DataWriter writer = new DataWriter();
            writer.ByteOrder = ByteOrder.LittleEndian;
            writer.WriteBytes(data);
            return writer.DetachBuffer();
        }
        public override void NotifyValue()
        {
            base.NotifyValue();
        }
    }
    public class SteeringCharacteristicAngle : GenericGattCharacteristic
    {
        private Timer ticker = null;
        SteeringCharacteristicAuthTx m_tx;
        public SteeringCharacteristicAngle(GattLocalCharacteristic characteristic, SteeringCharacteristicAuthTx tx) : base(characteristic)
        {
            m_tx = tx;
            ticker = new Timer(Update, "", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        }
        private void Update(Object state)
        {
            Calc();
        }
        public static IBuffer ToIBuffer(byte[] data)
        {
            DataWriter writer = new DataWriter();
            writer.ByteOrder = ByteOrder.LittleEndian;
            writer.WriteBytes(data);
            return writer.DetachBuffer();
        }
        public static float m_pos = 0, m_sent_pos = -1;
        public static int m_sent_time = 0;
        [DllImport("user32.dll")]
        static extern int GetAsyncKeyState(int key);
        private void Calc()
        {
            if (!m_tx.Authenticate())
                return;
            if (m_sent_pos != m_pos || Environment.TickCount - m_sent_time > 100)
            {
                m_sent_time = Environment.TickCount;
                m_sent_pos = m_pos;
                Value = ToIBuffer(BitConverter.GetBytes(m_pos));
                if (0 == GetAsyncKeyState(0x25) && 0 == GetAsyncKeyState(0x27)) //no left and right
                {
                    if (m_pos >= 4) m_pos -= 4;
                    else if (m_pos <= -4) m_pos += 4;
                    else m_pos = 0;
                }
                NotifyValue();
            }
        }
        public override void NotifyValue()
        {
            base.NotifyValue();
        }
    }
    internal class Program {
        [DllImport("user32.dll")]
        static extern int GetAsyncKeyState(int key);
        static private ILogger _logger;
        static private IGattServer _gattServerPower, _gattServerSteer;
        static private PowerMeasurementCharacteristic _myPMCharacteristic;
        static private SteeringCharacteristicAuthTx _mySAuthCharacteristicTx;
        static private SteeringCharacteristicAngle _mySAngleCharacteristic;

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
            _gattServerPower = new Common.GattServer(GattServiceUuids.CyclingPower, _logger);
            _gattServerSteer = new Common.GattServer(new Guid("347b0001-7635-408b-8918-8ff3949ce592"), _logger);
            _gattServerSteer.OnCharacteristicWrite += GattServerOnCharacteristicWrite;
        }
        private static async Task StartGattServer()
        {
            try
            {
                await _logger.LogMessageAsync("Starting Initializong Jenx.si Bluetooth Gatt service.");
                await _gattServerPower.Initialize();
                await _gattServerSteer.Initialize();
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
            var myCharacteristicFeature = await _gattServerPower.AddReadCharacteristicAsync(GattCharacteristicUuids.CyclingPowerFeature, new byte[] { 0, 0 }, "Power feature");
            var myCharacteristicLoc = await _gattServerPower.AddReadCharacteristicAsync(GattCharacteristicUuids.SensorLocation, new byte[] { 1 }, "Sensor location");
            var myCharacteristic = await _gattServerPower.AddNotifyCharacteristicAsync(GattCharacteristicUuids.CyclingPowerMeasurement, "Power & Cadence Measurement");

            if (myCharacteristic == null)
                return;

            _myPMCharacteristic = new PowerMeasurementCharacteristic(myCharacteristic);

            var mySteerCharacteristicAuthTx = //await _gattServerSteer.AddNotifyCharacteristicAsync(new Guid("347b0032-7635-408b-8918-8ff3949ce592"), "Steering Auth");
                await _gattServerSteer.AddReadIndicateCharacteristicAsync(new Guid("347b0032-7635-408b-8918-8ff3949ce592"), new byte[] { 0xff, 0x13, 0xFF }, "Steering Auth");
            var mySteerCharacteristicAuthRx = await _gattServerSteer.AddWriteCharacteristicAsync(new Guid("347b0031-7635-408b-8918-8ff3949ce592"), "Steering Auth");
            var mySteerCharacteristicAngle = await _gattServerSteer.AddNotifyCharacteristicAsync(new Guid("347b0030-7635-408b-8918-8ff3949ce592"), "Steering Angle");
            _mySAuthCharacteristicTx = new SteeringCharacteristicAuthTx(mySteerCharacteristicAuthTx);
            _mySAngleCharacteristic = new SteeringCharacteristicAngle(mySteerCharacteristicAngle, _mySAuthCharacteristicTx);

            _gattServerPower.Start();
            _gattServerSteer.Start();
            await _logger.LogMessageAsync("Jenx.si Bluetooth Gatt service started.");
        }
        static Random rg = new Random(0);
        private static async Task StartLooping()
        {
            System.ConsoleKeyInfo cki;
            System.Console.CancelKeyPress += new System.ConsoleCancelEventHandler(KeyPressHandler);
            await _logger.LogMessageAsync("Press any key, or 'X' to quit, or ");
            await _logger.LogMessageAsync("CTRL+C to interrupt the read operation:");
            int lastUp = Environment.TickCount, lastDown = Environment.TickCount, lastLeft = Environment.TickCount, lastRight = Environment.TickCount;
            while (true)
            {
                //cki = System.Console.ReadKey(true);
                //await _logger.LogMessageAsync($"  Key pressed: {cki.Key}\n");

                // Exit if the user pressed the 'X' key.
                //if (cki.Key == System.ConsoleKey.X) break;
                int delta = (rg.Next() % 5) + 20;
                int time = Environment.TickCount;
                if (0 != GetAsyncKeyState(0x26) && PowerMeasurementCharacteristic.m_power < 500)
                {
                    if (time - lastUp > 100)
                    {
                        lastUp = time;
                        PowerMeasurementCharacteristic.m_power += delta;
                        //await _logger.LogMessageAsync($"  Power: {PowerMeasurementCharacteristic.m_power}\n");
                    }
                }
                if (0 != GetAsyncKeyState(0x28) && PowerMeasurementCharacteristic.m_power > delta)
                {
                    if (time - lastDown > 100)
                    {
                        lastDown = time;
                        PowerMeasurementCharacteristic.m_power -= delta;
                        //await _logger.LogMessageAsync($"  Power: {PowerMeasurementCharacteristic.m_power}\n");
                    }
                }
                if (0 != GetAsyncKeyState(0x25) && SteeringCharacteristicAngle.m_pos > -40)
                {
                    if (time - lastLeft > 50)
                    {
                        lastLeft = time;
                        if (SteeringCharacteristicAngle.m_pos >= 0) SteeringCharacteristicAngle.m_pos = -4;
                        SteeringCharacteristicAngle.m_pos -= 1;
                        //await _logger.LogMessageAsync($"  m_pos: {SteeringCharacteristicAngle.m_pos }\n");
                    }
                }
                if (0 != GetAsyncKeyState(0x27) && SteeringCharacteristicAngle.m_pos < 40)
                {
                    if (time - lastRight > 50)
                    {
                        lastRight = time;
                        if (SteeringCharacteristicAngle.m_pos <= 0) SteeringCharacteristicAngle.m_pos = 4;
                        SteeringCharacteristicAngle.m_pos += 1;
                        //await _logger.LogMessageAsync($"  m_pos: {SteeringCharacteristicAngle.m_pos }\n");
                    }
                }
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
            await _mySAuthCharacteristicTx.OnControl();
        }

        private static void StopGattServer()
        {
            _gattServerPower.Stop();
        }

        #endregion Private
    }
}