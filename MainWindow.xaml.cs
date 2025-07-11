using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media; // For SolidColorBrush
using System.Windows.Threading; // For DispatcherTimer
using Wpf.Ui.Controls;
using TextBlock = Wpf.Ui.Controls.TextBlock; // For FluentWindow, ContentDialog etc.

namespace NetChanger
{
    public partial class MainWindow : FluentWindow
    {
        private ObservableCollection<NetworkAdapterInfo> _networkAdapters = new ObservableCollection<NetworkAdapterInfo>();
        private Dictionary<string, string> _modemIps = new Dictionary<string, string>();
        private Dictionary<string, string[]> _dnsServers = new Dictionary<string, string[]>();
        private DispatcherTimer _statusClearTimer;

        public MainWindow()
        {
            InitializeComponent();
            cmbNetworkAdapters.ItemsSource = _networkAdapters;

            // Initialize DispatcherTimer for clearing action status message
            _statusClearTimer = new DispatcherTimer();
            _statusClearTimer.Interval = TimeSpan.FromSeconds(5); // Message disappears after 5 seconds
            _statusClearTimer.Tick += StatusClearTimer_Tick;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Set window size to 40% of current screen resolution
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            this.Width = screenWidth * 0.40;
            this.Height = screenHeight * 0.40;

            LoadConfiguration();
            PopulateNetworkAdapters();
            PopulateDropdowns();

            if (cmbNetworkAdapters.Items.Count > 0)
            {
                cmbNetworkAdapters.SelectedIndex = 0;
                cmbNetworkAdapters_SelectionChanged(null, null);
            }
        }

        private void StatusClearTimer_Tick(object sender, EventArgs e)
        {
            _statusClearTimer.Stop();
            txtActionButtonStatus.Text = string.Empty;
            txtActionButtonStatus.Visibility = Visibility.Collapsed;
        }

        private void LoadConfiguration()
        {
            string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            _modemIps.Clear();
            _dnsServers.Clear();

            if (!File.Exists(configFilePath))
            {
                ShowContentDialog("Configuration Error", "Error: config.txt not found. Please create it in the application directory.", true);
                ShowStatus("Failed to load configuration.", true); // General status
                return;
            }

            string[] lines = File.ReadAllLines(configFilePath);
            string currentSection = string.Empty;

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
                {
                    continue;
                }

                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                    continue;
                }

                if (currentSection == "Modems")
                {
                    string[] parts = trimmedLine.Split(new char[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        _modemIps[parts[0].Trim()] = parts[1].Trim();
                    }
                }
                else if (currentSection == "DNS")
                {
                    string[] parts = trimmedLine.Split(new char[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        string[] dnsIps = parts[1].Trim().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                  .Select(ip => ip.Trim()).ToArray();
                        _dnsServers[parts[0].Trim()] = dnsIps;
                    }
                }
            }

            ShowStatus("Configuration loaded successfully.", false);
        }

        private void PopulateNetworkAdapters()
        {
            _networkAdapters.Clear();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                {
                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    if (ipProps.UnicastAddresses.Any(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                    {
                        _networkAdapters.Add(new NetworkAdapterInfo { Id = ni.Id, Description = ni.Description });
                    }
                }
            }

            if (_networkAdapters.Any())
            {
                cmbNetworkAdapters.SelectionChanged += cmbNetworkAdapters_SelectionChanged;
                ShowStatus($"Found {_networkAdapters.Count} active network adapters.", false);
            }
            else
            {
                ShowContentDialog("Network Adapter Error", "No active Ethernet or Wi-Fi adapters found. Please ensure your network adapter is connected and enabled.", true);
                ShowStatus("No active network adapters found.", true); // General status
            }
        }

        private void PopulateDropdowns()
        {
            cmbModems.Items.Clear();
            foreach (var modem in _modemIps)
            {
                ComboBoxItem item = new ComboBoxItem
                {
                    Content = modem.Key + " (" + modem.Value + ")",
                    Tag = modem.Value
                };
                cmbModems.Items.Add(item);
            }
            if (cmbModems.Items.Count > 0) cmbModems.SelectedIndex = 0;

            cmbDnsServers.Items.Clear();
            foreach (var dns in _dnsServers)
            {
                ComboBoxItem item = new ComboBoxItem
                {
                    Content = dns.Key + " (" + string.Join(", ", dns.Value) + ")",
                    Tag = dns.Value
                };
                cmbDnsServers.Items.Add(item);
            }
            if (cmbDnsServers.Items.Count > 0) cmbDnsServers.SelectedIndex = 0;
        }

        private void cmbNetworkAdapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbNetworkAdapters.SelectedItem == null)
            {
                txtStaticIp.Text = "";
                return;
            }

            string adapterId = ((NetworkAdapterInfo)cmbNetworkAdapters.SelectedItem).Id;
            RetrieveCurrentIpAndSubnet(adapterId);
        }

        private void RetrieveCurrentIpAndSubnet(string adapterId)
        {
            try
            {
                ManagementClass mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
                ManagementObjectCollection moc = mc.GetInstances();

                foreach (ManagementObject mo in moc)
                {
                    if (mo["SettingID"] != null && mo["SettingID"].ToString() == adapterId)
                    {
                        string[] ipAddresses = (string[])mo["IPAddress"];
                        string[] subnetMasks = (string[])mo["IPSubnet"];

                        if (ipAddresses != null && ipAddresses.Length > 0 && subnetMasks != null && subnetMasks.Length > 0)
                        {
                            txtStaticIp.Text = ipAddresses[0];
                            ShowStatus($"Current IP loaded for {((NetworkAdapterInfo)cmbNetworkAdapters.SelectedItem).Description}.", false);
                        }
                        else
                        {
                            txtStaticIp.Text = "";
                            ShowContentDialog("IP Configuration Warning", "No IPv4 address found for selected adapter. Please ensure it has a static IPv4 configuration.", true);
                            ShowStatus("No IPv4 address found for adapter.", true); // General status
                        }
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowContentDialog("Retrieval Error", $"Error retrieving current IP: {ex.Message}", true);
                ShowStatus($"Error retrieving current IP: {ex.Message}", true); // General status
                txtStaticIp.Text = "";
            }
        }

        private void ApplySettings_Click(object sender, RoutedEventArgs e)
        {
            ShowActionButtonStatus(string.Empty, false, clearImmediate: true); // Clear previous status
            ShowStatus("Validating inputs...", false); // General status

            if (cmbNetworkAdapters.SelectedItem == null)
            {
                ShowActionButtonStatus("Please select a network adapter.", false);
                return;
            }

            if (cmbModems.SelectedItem == null)
            {
                ShowActionButtonStatus("Please select a modem (gateway).", false);
                return;
            }

            if (cmbDnsServers.SelectedItem == null)
            {
                ShowActionButtonStatus("Please select DNS servers.", false);
                return;
            }

            string adapterId = ((NetworkAdapterInfo)cmbNetworkAdapters.SelectedItem).Id;
            string selectedGateway = ((ComboBoxItem)cmbModems.SelectedItem).Tag.ToString();
            string[] selectedDns = (string[])((ComboBoxItem)cmbDnsServers.SelectedItem).Tag;

            string currentIp = txtStaticIp.Text.Trim();

            string currentSubnet = GetCurrentSubnetMask(adapterId);
            if (string.IsNullOrEmpty(currentSubnet))
            {
                ShowActionButtonStatus("Could not retrieve current Subnet Mask. Cannot proceed.", false);
                return;
            }

            if (!System.Net.IPAddress.TryParse(currentIp, out _))
            {
                ShowActionButtonStatus("Invalid Static IP Address format.", false);
                return;
            }
            if (!System.Net.IPAddress.TryParse(selectedGateway, out _))
            {
                ShowActionButtonStatus("Invalid Gateway IP Address format.", false);
                return;
            }
            foreach (string dns in selectedDns)
            {
                if (!System.Net.IPAddress.TryParse(dns, out _))
                {
                    ShowActionButtonStatus($"Invalid DNS IP Address format: {dns}", false);
                    return;
                }
            }

            SetNetworkConfiguration(adapterId, currentIp, currentSubnet, selectedGateway, selectedDns);
        }

        private void SetNetworkConfiguration(string adapterId, string ipAddress, string subnetMask, string gateway, string[] dnsServers)
        {
            try
            {
                ShowStatus("Applying network settings...", false);

                ManagementClass mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
                ManagementObjectCollection moc = mc.GetInstances();

                foreach (ManagementObject mo in moc)
                {
                    if (mo["SettingID"] != null && mo["SettingID"].ToString() == adapterId)
                    {
                        ManagementBaseObject newIP = mo.GetMethodParameters("EnableStatic");
                        newIP["IPAddress"] = new string[] { ipAddress };
                        newIP["SubnetMask"] = new string[] { subnetMask };
                        ManagementBaseObject setIP = mo.InvokeMethod("EnableStatic", newIP, null);

                        if (setIP["ReturnValue"].ToString() != "0")
                        {
                            string errorMessage = $"Failed to confirm/set IP Address and Subnet Mask: {GetWmiErrorDescription(setIP["ReturnValue"])}";
                            ShowContentDialog("Network Configuration Error", errorMessage, true);
                            ShowActionButtonStatus("Failed to apply settings!", false);
                            return;
                        }

                        ManagementBaseObject newGateway = mo.GetMethodParameters("SetGateways");
                        newGateway["DefaultIPGateway"] = new string[] { gateway };
                        newGateway["GatewayCostMetric"] = new int[] { 1 };
                        ManagementBaseObject setGateway = mo.InvokeMethod("SetGateways", newGateway, null);

                        if (setGateway["ReturnValue"].ToString() != "0")
                        {
                            string errorMessage = $"Failed to set Gateway: {GetWmiErrorDescription(setGateway["ReturnValue"])}";
                            ShowContentDialog("Network Configuration Error", errorMessage, true);
                            ShowActionButtonStatus("Failed to apply settings!", false);
                            return;
                        }

                        ManagementBaseObject newDNS = mo.GetMethodParameters("SetDNSServerSearchOrder");
                        newDNS["DNSServerSearchOrder"] = dnsServers;
                        ManagementBaseObject setDNS = mo.InvokeMethod("SetDNSServerSearchOrder", newDNS, null);

                        if (setDNS["ReturnValue"].ToString() != "0")
                        {
                            string errorMessage = $"Failed to set DNS Servers: {GetWmiErrorDescription(setDNS["ReturnValue"])}";
                            ShowContentDialog("Network Configuration Error", errorMessage, true);
                            ShowActionButtonStatus("Failed to apply settings!", false);
                            return;
                        }

                        ShowStatus($"Successfully applied settings for {((NetworkAdapterInfo)cmbNetworkAdapters.SelectedItem).Description}.", false);
                        ShowActionButtonStatus("Settings Saved!", true);
                        return;
                    }
                }
                ShowActionButtonStatus("Selected network adapter not found or not configured.", false);
            }
            catch (UnauthorizedAccessException)
            {
                ShowContentDialog("Access Denied", "Please run the application as Administrator to change network settings.", true);
                ShowActionButtonStatus("Access Denied!", false);
            }
            catch (Exception ex)
            {
                ShowContentDialog("Application Error", $"An unexpected error occurred: {ex.Message}", true);
                ShowActionButtonStatus("An error occurred!", false);
            }
        }

        private string GetCurrentSubnetMask(string adapterId)
        {
            try
            {
                ManagementClass mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
                ManagementObjectCollection moc = mc.GetInstances();

                foreach (ManagementObject mo in moc)
                {
                    if (mo["SettingID"] != null && mo["SettingID"].ToString() == adapterId)
                    {
                        string[] subnetMasks = (string[])mo["IPSubnet"];
                        if (subnetMasks != null && subnetMasks.Length > 0)
                        {
                            return subnetMasks[0];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Error retrieving current Subnet Mask: {ex.Message}", true); // General status for retrieval error
            }
            return null;
        }

        // Updated ShowStatus for general messages (txtStatusMessage)
        private void ShowStatus(string message, bool isError)
        {
            txtStatusMessage.Dispatcher.Invoke(() =>
            {
                txtStatusMessage.Text = message;
                txtStatusMessage.Foreground = isError ? System.Windows.Media.Brushes.Red : (System.Windows.Media.Brush)Application.Current.FindResource("TextFillColorPrimaryBrush");
            });
        }

        // New method to show temporary status messages near the button
        private void ShowActionButtonStatus(string message, bool isSuccess, bool clearImmediate = false)
        {
            txtActionButtonStatus.Dispatcher.Invoke(() =>
            {
                _statusClearTimer.Stop(); // Stop any previous timer
                txtActionButtonStatus.Text = message;
                txtActionButtonStatus.Foreground = isSuccess ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
                txtActionButtonStatus.Visibility = Visibility.Visible;

                if (!clearImmediate)
                {
                    _statusClearTimer.Start(); // Start timer to clear message
                }
                else
                {
                    txtActionButtonStatus.Visibility = Visibility.Collapsed; // Clear immediately
                }
            });
        }

        // New method to show ContentDialogs for critical errors
        private async void ShowContentDialog(string title, string message, bool isError)
        {
            var contentDialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = isError ? Brushes.Red : Brushes.Black },
                CloseButtonText = "Close"
            };

            await contentDialog.ShowAsync();
        }

        private string GetWmiErrorDescription(object returnValue)
        {
            if (returnValue == null) return "Unknown Error";
            switch (returnValue.ToString())
            {
                case "0": return "Successful";
                case "1": return "Not Supported";
                case "2": return "Unknown Failure";
                case "3": return "Invalid Subnet Mask";
                case "4": return "Invalid Gateway";
                case "5": return "Invalid IP Address";
                case "13": return "Invalid DNSServerSearchOrder";
                default: return $"WMI Return Code: {returnValue}";
            }
        }
    }

    public class NetworkAdapterInfo
    {
        public string Id { get; set; }
        public string Description { get; set; }
    }
}