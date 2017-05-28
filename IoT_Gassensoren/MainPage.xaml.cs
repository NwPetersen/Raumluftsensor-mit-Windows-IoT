using System;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using ADC.Devices.I2c.ADS1115;
using Microsoft.IoT.Lightning.Providers;
using Windows.Devices;
using System.Diagnostics;
using Windows.UI.Xaml.Navigation;
using ThingSpeakWinRT;

/*******************************************************************************
 * *********************MG-811 Gas Sensor Module********************************
 * 
 * Author: Niklas Petersen
 *         Thorben Brodersen
 *          
 *         This Code uses Parts from Sandbox Electronics Code for the MG-811 Gas
 *         Sensor Modul (https://www.dfrobot.com/wiki/index.php/CO2_Sensor_SKU:SEN0159) 
 *         and Code from Máté Horváth for the ADS1115 16-bit ADC (https://github.com/horvathm/ads1115-16bit-adc).
 *         
 *         And a big Thanks to Chris Pietschmann for his great work with the BME280 https://www.hackster.io/23021/weather-station-v-3-0-b8b8bc!
 * 
 * Note:    The Equation for the CO2 Sensor is in the CO2Timer_Tick Methode. 
 * 
 * *****************************************************************************/

namespace IoT_Gassensoren
{
    public sealed partial class MainPage : Page
    {
        #region Fields

        //Timer:
        DispatcherTimer BME280timer;
        private DispatcherTimer CO2timer;
        private DispatcherTimer thingSpeakTimer;

        //ADC´s:
        private ADS1115Sensor adc;
        BuildAzure.IoT.Adafruit.BME280.BME280Sensor _bme280;

        //Variables:
        const float seaLevelPressure = 1022.00f;
        private double aCO2;
        private string aTemp;
        private string aHumidity;
        private string aPressure;
        private string aAltitude;
        #endregion

        #region INotifyPropertyChanged implementation

        public bool Set<T>(ref T storage, T value, [CallerMemberName]string propertyName = null)
        {
            // if unchanged return false
            if (Equals(storage, value))
                return false;
            storage = value;
            return true;
        }
        #endregion

        #region Properties
        public double ConvertedValue
        {
            get { return _convertedValue; }
            set { Set(ref _convertedValue, value); }
        }
        private double _convertedValue = 0;

        public double ConvertedVoltage
        {
            get { return _convertedVoltage; }
            set { Set(ref _convertedVoltage, value); }
        }
        private double _convertedVoltage = 0;

        public ADS1115SensorSetting Setting
        {
            get { return _setting; }
            set { Set(ref _setting, value); }
        }
        private ADS1115SensorSetting _setting = new ADS1115SensorSetting();
        
        #endregion

        public MainPage()
        {
            this.InitializeComponent();

            // Setting the DataContext
            this.DataContext = this;

            // Register for the unloaded event so we can clean up upon exit
            Unloaded += MainPage_Unloaded;

            // Set Lightning as the default provider
            if (LightningProvider.IsLightningEnabled)
                LowLevelDevicesController.DefaultProvider = LightningProvider.GetAggregateProvider();

            // Initialize the DispatcherTimer for CO2 Sensor
            CO2timer = new DispatcherTimer();
            CO2timer.Interval = TimeSpan.FromSeconds(5);
            CO2timer.Tick += CO2Timer_Tick;

            // Initialize the DispatcherTimer for sending Data to ThingSpeak
            thingSpeakTimer = new DispatcherTimer();
            thingSpeakTimer.Interval = TimeSpan.FromSeconds(120);
            thingSpeakTimer.Tick += ThingSpeakTimer_Tick;
            thingSpeakTimer.Start();

            // Initialize the sensors
            InitializeSensors();
        }

        #region Timer Tick
        private async void ThingSpeakTimer_Tick(object sender, object e)
        {
            try
            {
                var client = new ThingSpeakClient(false);

                var dataFeed = new ThingSpeakFeed { Field1 = aCO2.ToString(), Field2 = aTemp, Field3 = aPressure, Field4 = aHumidity };
                dataFeed = await client.UpdateFeedAsync("Your API Key", dataFeed);                                                          //Fill in your API Key!
                Debug.WriteLine("Daten gesendet!");
            }
            catch(Exception ex)
            {
                throw new Exception("Sending Data has failed" + ex);
            }


        }

        private void CO2Timer_Tick(object sender, object e)
        {
            
            if (adc != null && adc.IsInitialized)
            {
                try
                {
                    var temp = adc.readContinuous();
                    ConvertedVoltage = 0;
                    ConvertedValue = temp;


                    ConvertedVoltage = (double)temp * (6.144 / (double)32768);
                    //Debug.WriteLine("Voltage: {0}", ConvertedVoltage);
                    //Debug.WriteLine("Value: {0}", temp);

                    #region CO2 Sensor
                    double ZERO_POINT_X = 2.602; //lg400=2.602, the start point_on X_axis of the curve
                    double ZERO_POINT_VOLTAGE = 0.306; //define the output of the sensor in volts when the concentration of CO2 is 400PPM
                    //double MAX_POINT_VOLTAGE = 0.244; //define the output of the sensor in volts when the concentration of CO2 is 10,000PPM -- We don´t need this one with our Sensor
                    double REACTION_VOLTGAE = 0.180; //define the voltage drop of the sensor when move the sensor from air into 1000ppm CO2

                    aCO2 = Math.Pow(10, (((ConvertedVoltage / 8.5) - ZERO_POINT_VOLTAGE) / (REACTION_VOLTGAE / (ZERO_POINT_X - 4)) + ZERO_POINT_X));
                    Debug.WriteLine("\nCO2: {0}\n", aCO2);
                    
                    #endregion

                }
                catch (Exception ex)
                {
                    throw new Exception("Continuous read has failed" + ex);
                }
                
            }
        }

        private async void BME280Timer_Tick(object sender, object e)
        {
            var temp = await _bme280.ReadTemperature();
            var humidity = await _bme280.ReadHumidity();
            var pressure = await _bme280.ReadPressure();
            var altitude = await _bme280.ReadAltitude(seaLevelPressure);

            aTemp = temp.ToString();
            aHumidity = humidity.ToString();
            aPressure = pressure.ToString();
            aAltitude = altitude.ToString();

            Debug.WriteLine("Temp: {0} deg C", temp);
            Debug.WriteLine("Humidity: {0} %", humidity);
            Debug.WriteLine("Pressure: {0} Pa", pressure);
            Debug.WriteLine("Altitude: {0} m", altitude);
        }
        #endregion

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {

            if (adc != null)
            {
                adc.Dispose();
                adc = null;
            };

            CO2timer.Stop();
            CO2timer = null;

            BME280timer.Stop();
            BME280timer = null;

            thingSpeakTimer.Stop();
            thingSpeakTimer = null;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _bme280 = new BuildAzure.IoT.Adafruit.BME280.BME280Sensor();
            await _bme280.Initialize();

            BME280timer = new DispatcherTimer();
            BME280timer.Interval = TimeSpan.FromSeconds(5);
            BME280timer.Tick += BME280Timer_Tick;

            BME280timer.Start();
        }

        private async void InitializeSensors()
        {
            try
            {
                adc = new ADS1115Sensor(AdcAddress.GND);
                AdcSetSettings();

                await adc.InitializeAsync();
            }
            catch (Exception ex)
            {
                throw new Exception("Initialization has failed: " + ex);
            }

            AdcDataRead();

        }

        private async void AdcDataRead()
        {
            if (adc != null && adc.IsInitialized)
            {
                try
                {
                    await adc.readContinuousInit(Setting);
                }
                catch (Exception ex)
                {
                    throw new Exception("Initialization of continuous read has failed" + ex);
                }

                CO2timer.Start();
            }
        }

        private void AdcSetSettings()
        {
            Setting.Mode = AdcMode.CONTINOUS_CONVERSION;
            Setting.Input = AdcInput.A0_SE;
            Setting.DataRate = AdcDataRate.SPS128;
            Setting.Pga = AdcPga.G2P3;
            Setting.ComMode = AdcComparatorMode.TRADITIONAL;
            Setting.ComPolarity = AdcComparatorPolarity.ACTIVE_LOW;
            Setting.ComLatching = AdcComparatorLatching.LATCHING;
            Setting.ComQueue = AdcComparatorQueue.DISABLE_COMPARATOR;
        }
    }
}
