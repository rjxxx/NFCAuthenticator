﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.ApplicationModel.Background;
using Windows.Data.Xml.Dom;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Security.Authentication.Identity.Provider;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using Windows.UI.Notifications;

namespace Tasks
{
    public sealed class BGTask : IBackgroundTask
    {
        ManualResetEvent opCompletedEvent = null;
        SerialDevice _serialPort;
        DataWriter _dw;
        DataReader _dr;


        [Obsolete]
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            var deferral = taskInstance.GetDeferral();

            // This event is signaled when the operation completes
            opCompletedEvent = new ManualResetEvent(false);
            SecondaryAuthenticationFactorAuthentication.AuthenticationStageChanged += OnStageChanged;
            
            // Wait until the operation completes
            opCompletedEvent.WaitOne();
            deferral.Complete();
        }


        int Search(byte[] src, byte[] pattern)
        {
            int maxFirstCharSlot = src.Length - pattern.Length + 1;
            for (int i = 0; i < maxFirstCharSlot; i++)
            {
                if (src[i] != pattern[0]) // compare only first byte
                    continue;

                // found a match on first byte, now try to match rest of the pattern
                for (int j = pattern.Length - 1; j >= 1; j--)
                {
                    if (src[i + j] != pattern[j]) break;
                    if (j == 1) return i;
                }
            }
            return -1;
        }


        [Obsolete]
        async void PerformAuthentication()
        {
            //ShowToastNotification("Performing Auth!");

            //Get the selected device from app settings
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            String m_selectedDeviceId = localSettings.Values["SelectedDevice"] as String;

            SecondaryAuthenticationFactorAuthenticationStageInfo authStageInfo = await SecondaryAuthenticationFactorAuthentication.GetAuthenticationStageInfoAsync();

            if (authStageInfo.Stage != SecondaryAuthenticationFactorAuthenticationStage.CollectingCredential)
            {
                ShowToastNotification("Unexpected!");
                throw new Exception("Unexpected!");
            }

            //ShowToastNotification("Post Collecting Credential");

            IReadOnlyList<SecondaryAuthenticationFactorInfo> deviceList = await SecondaryAuthenticationFactorRegistration.FindAllRegisteredDeviceInfoAsync(
                    SecondaryAuthenticationFactorDeviceFindScope.AllUsers);

            if (deviceList.Count == 0)
            {
                ShowToastNotification("Unexpected exception, device list = 0");
                throw new Exception("Unexpected exception, device list = 0");
            }

            //ShowToastNotification("Found companion devices");

            SecondaryAuthenticationFactorInfo deviceInfo = deviceList.ElementAt(0);
            m_selectedDeviceId = deviceInfo.DeviceId;

            //ShowToastNotification("Device ID: " + m_selectedDeviceId);

            //a nonce is an arbitrary number that may only be used once - a random or pseudo-random number issued in an authentication protocol to ensure that old communications cannot be reused in replay attacks.
            IBuffer svcNonce = CryptographicBuffer.GenerateRandom(32);  //Generate a nonce and do a HMAC operation with the nonce


            //In real world, you would need to take this nonce and send to companion device to perform an HMAC operation with it
            //You will have only 20 second to get the HMAC from the companion device
            SecondaryAuthenticationFactorAuthenticationResult authResult = await SecondaryAuthenticationFactorAuthentication.StartAuthenticationAsync(
                    m_selectedDeviceId, svcNonce);

            if (authResult.Status != SecondaryAuthenticationFactorAuthenticationStatus.Started)
            {
                ShowToastNotification("Unexpected! Could not start authentication!");
                throw new Exception("Unexpected! Could not start authentication!");
            }

            //ShowToastNotification("Auth Started");

            //
            // WARNING: Test code
            // The HAMC calculation SHOULD be done on companion device
            //
            byte[] combinedDataArray;
            CryptographicBuffer.CopyToByteArray(authResult.Authentication.DeviceConfigurationData, out combinedDataArray);

            byte[] deviceKeyArray = new byte[32];
            byte[] authKeyArray = new byte[32];
            for (int index = 0; index < deviceKeyArray.Length; index++)
            {
                deviceKeyArray[index] = combinedDataArray[index];
            }
            for (int index = 0; index < authKeyArray.Length; index++)
            {
                authKeyArray[index] = combinedDataArray[deviceKeyArray.Length + index];
            }
            // Create device key and authentication key
            IBuffer deviceKey = CryptographicBuffer.CreateFromByteArray(deviceKeyArray);
            IBuffer authKey = CryptographicBuffer.CreateFromByteArray(authKeyArray);

            // Calculate the HMAC
            MacAlgorithmProvider hMACSha256Provider = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha256);

            CryptographicKey deviceHmacKey = hMACSha256Provider.CreateKey(deviceKey);
            IBuffer deviceHmac = CryptographicEngine.Sign(deviceHmacKey, authResult.Authentication.DeviceNonce);

            // sessionHmac = HMAC(authKey, deviceHmac || sessionNonce)
            IBuffer sessionHmac;
            byte[] deviceHmacArray = { 0 };
            CryptographicBuffer.CopyToByteArray(deviceHmac, out deviceHmacArray);

            byte[] sessionNonceArray = { 0 };
            CryptographicBuffer.CopyToByteArray(authResult.Authentication.SessionNonce, out sessionNonceArray);

            combinedDataArray = new byte[deviceHmacArray.Length + sessionNonceArray.Length];
            for (int index = 0; index < deviceHmacArray.Length; index++)
            {
                combinedDataArray[index] = deviceHmacArray[index];
            }
            for (int index = 0; index < sessionNonceArray.Length; index++)
            {
                combinedDataArray[deviceHmacArray.Length + index] = sessionNonceArray[index];
            }

            // Get a Ibuffer from combinedDataArray
            IBuffer sessionMessage = CryptographicBuffer.CreateFromByteArray(combinedDataArray);

            // Calculate sessionHmac
            CryptographicKey authHmacKey = hMACSha256Provider.CreateKey(authKey);
            sessionHmac = CryptographicEngine.Sign(authHmacKey, sessionMessage);

            //ShowToastNotification("Before finish auth");

            SecondaryAuthenticationFactorFinishAuthenticationStatus authStatus = await authResult.Authentication.FinishAuthenticationAsync(deviceHmac,
                sessionHmac);

            if (authStatus != SecondaryAuthenticationFactorFinishAuthenticationStatus.Completed)
            {
                ShowToastNotification("Unable to complete authentication!");
                throw new Exception("Unable to complete authentication!");
            }

            //ShowToastNotification("Auth completed");
        }

        public static void ShowToastNotification(string message)
        {

            ToastTemplateType toastTemplate = ToastTemplateType.ToastImageAndText01;
            XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(toastTemplate);

            // Set Text
            XmlNodeList toastTextElements = toastXml.GetElementsByTagName("text");
            toastTextElements[0].AppendChild(toastXml.CreateTextNode(message));

            // Set image
            // Images must be less than 200 KB in size and smaller than 1024 x 1024 pixels.
            XmlNodeList toastImageAttributes = toastXml.GetElementsByTagName("image");
            ((XmlElement)toastImageAttributes[0]).SetAttribute("src", "ms-appx:///Images/logo-80px-80px.png");
            ((XmlElement)toastImageAttributes[0]).SetAttribute("alt", "logo");

            // toast duration
            IXmlNode toastNode = toastXml.SelectSingleNode("/toast");
            ((XmlElement)toastNode).SetAttribute("duration", "short");

            // toast navigation
            var toastNavigationUriString = "#/MainPage.xaml?param1=12345";
            var toastElement = ((XmlElement)toastXml.SelectSingleNode("/toast"));
            toastElement.SetAttribute("launch", toastNavigationUriString);

            // Create the toast notification based on the XML content you've specified.
            ToastNotification toast = new ToastNotification(toastXml);

            // Send your toast notification.
            ToastNotificationManager.CreateToastNotifier().Show(toast);

        }

        // WARNING: Test code
        // This code should be in background task
        [Obsolete]
        async void OnStageChanged(Object sender, SecondaryAuthenticationFactorAuthenticationStageChangedEventArgs args)
        {

            //ShowToastNotification("In StageChanged!" + args.StageInfo.Stage.ToString());
            if (args.StageInfo.Stage == SecondaryAuthenticationFactorAuthenticationStage.WaitingForUserConfirmation)
            {
                //ShowToastNotification("Stage = WaitingForUserConfirmation");
                // This event is happening on a ThreadPool thread, so we need to dispatch to the UI thread.
                // Getting the dispatcher from the MainView works as long as we only have one view.
                String deviceName = "NFC authenticator";
                await SecondaryAuthenticationFactorAuthentication.ShowNotificationMessageAsync(
                    deviceName,
                    SecondaryAuthenticationFactorAuthenticationMessage.SwipeUpWelcome);
            }
            else if (args.StageInfo.Stage == SecondaryAuthenticationFactorAuthenticationStage.CollectingCredential)
            {
                //ShowToastNotification("Stage = CollectingCredential");

                var aqs = SerialDevice.GetDeviceSelector("COM4");

                var devices = await DeviceInformation.FindAllAsync(aqs, null);

                if (!devices.Any())
                {
                    ShowToastNotification("No device found");
                    return;
                }

                _serialPort = await SerialDevice.FromIdAsync(devices[0].Id);
                if (_serialPort == null)
                {
                    ShowToastNotification("Device not available");
                    return;
                }
                _serialPort.WriteTimeout = TimeSpan.FromMilliseconds(1000);
                _serialPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);
                _serialPort.BaudRate = 115200;
                _serialPort.Parity = SerialParity.None;
                _serialPort.StopBits = SerialStopBitCount.One;
                _serialPort.DataBits = 8;
                _serialPort.Handshake = SerialHandshake.None;
                _dw = new DataWriter(_serialPort.OutputStream);
                _dr = new DataReader(_serialPort.InputStream);


                byte[] writeByte = { 0x00, 0x00, 0xff, 0x04, 0xfc, 0xd4, 0x4a, 0x02, 0x00, 0xe0, 0x00 };
                const uint maxReadLength = 512;
                byte[] expected = { 244, 213, 75, 1, 1, 0, 4, 8, 4, 35, 172, 84, 167 };
                while (true)
                {
                    _dw.WriteBytes(writeByte);
                    await _dw.StoreAsync();


                    uint bytesToRead = await _dr.LoadAsync(maxReadLength);
                    byte[] rxBuffer = new byte[bytesToRead];
                    _dr.ReadBytes(rxBuffer);


                    if (Search(rxBuffer, expected) != -1)
                    {
                        PerformAuthentication();
                        return;
                    }
                    Thread.Sleep(300);
                }
            }
            else
            {
                if (args.StageInfo.Stage == SecondaryAuthenticationFactorAuthenticationStage.StoppingAuthentication)
                {
                    SecondaryAuthenticationFactorAuthentication.AuthenticationStageChanged -= OnStageChanged;
                    opCompletedEvent.Set();
                    if (_serialPort != null)
                    {
                        _serialPort.Dispose();
                        _serialPort = null;
                    }
                }

                SecondaryAuthenticationFactorAuthenticationStage stage = args.StageInfo.Stage;
            }
        }
    }
}
