using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Core;
using Windows.Security.Authentication.Identity.Provider;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Iot.Device.Pn532;
using Iot.Device.Pn532.ListPassive;
using System.Threading;
using Windows.Devices.SerialCommunication;
using Windows.Devices.Enumeration;
using System.Threading.Tasks;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;
using System.Runtime.InteropServices.WindowsRuntime;

// Документацию по шаблону элемента "Пустая страница" см. по адресу https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x419

namespace WindowsHelloNFC
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        string m_selectedDeviceId = string.Empty;
        bool taskRegistered = false;
        static string myBGTaskName = "BGTask";
        static string myBGTaskEntryPoint = "Tasks.BGTask";

        public MainPage()
        {
            this.InitializeComponent();

            DeviceListBox.SelectionChanged += DeviceListBox_SelectionChanged;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            IReadOnlyList<SecondaryAuthenticationFactorInfo> deviceList = await SecondaryAuthenticationFactorRegistration.FindAllRegisteredDeviceInfoAsync(SecondaryAuthenticationFactorDeviceFindScope.User);

            RefreshDeviceList(deviceList);

        }

        void RefreshDeviceList(IReadOnlyList<SecondaryAuthenticationFactorInfo> deviceList)
        {
            DeviceListBox.Items.Clear();

            for (int index = 0; index < deviceList.Count; ++index)
            {
                SecondaryAuthenticationFactorInfo deviceInfo = deviceList.ElementAt(index);
                DeviceListBox.Items.Add(deviceInfo.DeviceId);
            }
        }

        private async void RegisterDevice_Click(object sender, RoutedEventArgs e)
        {
            String deviceId = System.Guid.NewGuid().ToString();

            // WARNING: Test code
            // These keys should be generated on the companion device
            // Create device key and authentication key
            IBuffer deviceKey = CryptographicBuffer.GenerateRandom(32);
            IBuffer authKey = CryptographicBuffer.GenerateRandom(32);

            //
            // WARNING: Test code
            // The keys SHOULD NOT be saved into device config data
            //
            byte[] deviceKeyArray = { 0 };
            CryptographicBuffer.CopyToByteArray(deviceKey, out deviceKeyArray);

            byte[] authKeyArray = { 0 };
            CryptographicBuffer.CopyToByteArray(authKey, out authKeyArray);

            //Generate combinedDataArray
            int combinedDataArraySize = deviceKeyArray.Length + authKeyArray.Length;
            byte[] combinedDataArray = new byte[combinedDataArraySize];
            for (int index = 0; index < deviceKeyArray.Length; index++)
            {
                combinedDataArray[index] = deviceKeyArray[index];
            }
            for (int index = 0; index < authKeyArray.Length; index++)
            {
                combinedDataArray[deviceKeyArray.Length + index] = authKeyArray[index];
            }

            // Get a Ibuffer from combinedDataArray
            IBuffer deviceConfigData = CryptographicBuffer.CreateFromByteArray(combinedDataArray);

            //
            // WARNING: Test code
            // The friendly name and device model number SHOULD come from device
            //
            String deviceFriendlyName = "Test Simulator";
            String deviceModelNumber = "Sample A1";

            SecondaryAuthenticationFactorDeviceCapabilities capabilities = SecondaryAuthenticationFactorDeviceCapabilities.SecureStorage;

            SecondaryAuthenticationFactorRegistrationResult registrationResult = await SecondaryAuthenticationFactorRegistration.RequestStartRegisteringDeviceAsync(deviceId,
                    capabilities,
                    deviceFriendlyName,
                    deviceModelNumber,
                    deviceKey,
                    authKey);

            if (registrationResult.Status != SecondaryAuthenticationFactorRegistrationStatus.Started)
            {
                MessageDialog myDlg = null;

                if (registrationResult.Status == SecondaryAuthenticationFactorRegistrationStatus.DisabledByPolicy)
                {
                    //For DisaledByPolicy Exception:Ensure secondary auth is enabled.
                    //Use GPEdit.msc to update group policy to allow secondary auth
                    //Local Computer Policy\Computer Configuration\Administrative Templates\Windows Components\Microsoft Secondary Authentication Factor\Allow Companion device for secondary authentication
                    myDlg = new MessageDialog("Disabled by Policy.  Please update the policy and try again.");
                }

                if (registrationResult.Status == SecondaryAuthenticationFactorRegistrationStatus.PinSetupRequired)
                {
                    //For PinSetupRequired Exception:Ensure PIN is setup on the device
                    //Either use gpedit.msc or set reg key
                    //This setting can be enabled by creating the AllowDomainPINLogon REG_DWORD value under the HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System Registry key and setting it to 1.
                    myDlg = new MessageDialog("Please setup PIN for your device and try again.");
                }

                if (myDlg != null)
                {
                    await myDlg.ShowAsync();
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine("Device Registration Started!");
            await registrationResult.Registration.FinishRegisteringDeviceAsync(deviceConfigData);

            DeviceListBox.Items.Add(deviceId);
            System.Diagnostics.Debug.WriteLine("Device Registration is Complete!");

            IReadOnlyList<SecondaryAuthenticationFactorInfo> deviceList = await SecondaryAuthenticationFactorRegistration.FindAllRegisteredDeviceInfoAsync(
                SecondaryAuthenticationFactorDeviceFindScope.User);

            RefreshDeviceList(deviceList);
            RegisterTask();
        }

        private void DeviceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceListBox.Items.Count > 0)
            {
                m_selectedDeviceId = DeviceListBox.SelectedItem.ToString();
            }
            else
            {
                m_selectedDeviceId = String.Empty;
            }
            System.Diagnostics.Debug.WriteLine("The device " + m_selectedDeviceId + " is selected.");

            //Store the selected device in settings to be used in the BG task
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["SelectedDevice"] = m_selectedDeviceId;

        }

        private async void UnregisterDevice_Click(object sender, RoutedEventArgs e)
        {
            if (m_selectedDeviceId == String.Empty)
            {
                return;
            }

            //InfoList.Items.Add("Unregister a device:");

            await SecondaryAuthenticationFactorRegistration.UnregisterDeviceAsync(m_selectedDeviceId);

            //InfoList.Items.Add("Device unregistration is completed.");

            IReadOnlyList<SecondaryAuthenticationFactorInfo> deviceList = await SecondaryAuthenticationFactorRegistration.FindAllRegisteredDeviceInfoAsync(
                SecondaryAuthenticationFactorDeviceFindScope.User);

            RefreshDeviceList(deviceList);
        }



        async void TestDevice_Click(object sender, RoutedEventArgs e)
        {
            ushort vendorId = 0x2341;
            ushort productId = 0x8036;
            ushort usagePage = 0xFFC0;
            ushort usageId = 0x0C00;

            // Create the selector.
            string selector =
                HidDevice.GetDeviceSelector(usagePage, usageId, vendorId, productId);

            // Enumerate devices using the selector.
            var devices = await DeviceInformation.FindAllAsync(selector);

            if (devices.Any())
            {
                // At this point the device is available to communicate with
                // So we can send/receive HID reports from it or 
                // query it for control descriptions.
                System.Diagnostics.Debug.WriteLine("HID devices found: " + devices.Count);
                // Open the target HID device.

                HidDevice device =
                    await HidDevice.FromIdAsync(devices.ElementAt(0).Id,
                    FileAccessMode.ReadWrite);
                
  

                if (device != null)
                {
                    var outputReport = device.CreateOutputReport();

                    byte[] dataBytes = new byte[64];

                    var dataWriter = new DataWriter();

                    // First byte is always the report id
                    dataWriter.WriteByte((Byte)outputReport.Id);
                    dataWriter.WriteBytes(dataBytes);
         
                    outputReport.Data = dataWriter.DetachBuffer();

                    uint bytesWritten = await device.SendOutputReportAsync(outputReport);


                    // Input reports contain data from the device.
                    device.InputReportReceived += (sender1, args) =>
                    {
                        HidInputReport inputReport = args.Report;
                        IBuffer buffer = inputReport.Data;

                        System.Diagnostics.Debug.WriteLine("\nHID Input Report: " + inputReport.ToString() +
                            "\nTotal number of bytes received: " + buffer.Length.ToString());
                    };
                    //device.Dispose();
                }

            }
            else
            {
                // There were no HID devices that met the selector criteria.
                System.Diagnostics.Debug.WriteLine("HID device not found");
            }
        }

        private async void OnBgTaskProgress(BackgroundTaskRegistration sender, BackgroundTaskProgressEventArgs args)
        {
            // WARNING: Test code
            // Handle background task progress.
            if (args.Progress == 1)
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    System.Diagnostics.Debug.WriteLine("Background task is started.");
                });
            }
        }

        async void RegisterTask()
        {
            System.Diagnostics.Debug.WriteLine("Register the background task.");
            //
            // Check for existing registrations of this background task.
            //

            BackgroundExecutionManager.RemoveAccess();
            var access = await BackgroundExecutionManager.RequestAccessAsync();

            foreach (var task in BackgroundTaskRegistration.AllTasks)
            {
                if (task.Value.Name == myBGTaskName)
                {
                    taskRegistered = true;
                    //break;
                }
            }

            if (!taskRegistered)
            {

                if (access == BackgroundAccessStatus.AllowedSubjectToSystemPolicy)
                {
                    BackgroundTaskBuilder taskBuilder = new BackgroundTaskBuilder();
                    taskBuilder.Name = myBGTaskName;
                    // Create the trigger.
                    SecondaryAuthenticationFactorAuthenticationTrigger myTrigger = new SecondaryAuthenticationFactorAuthenticationTrigger();

                    taskBuilder.TaskEntryPoint = myBGTaskEntryPoint;
                    taskBuilder.SetTrigger(myTrigger);
                    BackgroundTaskRegistration taskReg = taskBuilder.Register();

                    String taskRegName = taskReg.Name;
                    //taskReg.Progress += OnBgTaskProgress;
                    System.Diagnostics.Debug.WriteLine("Background task registration is completed.");
                    taskRegistered = true;
                }
            }

        }
    }
}