using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management; // Required for WMI
using System.Net.NetworkInformation; // For NetworkInterface
using System.Windows;
using System.Windows.Controls;
// Add WPF UI namespace
using Wpf.Ui.Controls; // For FluentWindow, CardExpander etc.

namespace NetChanger
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow // Change Window to FluentWindow
    {
        private ObservableCollection<NetworkAdapterInfo> _networkAdapters = new ObservableCollection<NetworkAdapterInfo>();
        private Dictionary<string, string> _modemIps = new Dictionary<string, string>();
        private Dictionary<string, string[]> _dnsServers = new Dictionary<string, string[]>();

        public MainWindow()
        {
            InitializeComponent();
            // Assign ItemSource for cmbNetworkAdapters (it's an ObservableCollection and managed differently)
            cmbNetworkAdapters.ItemsSource = _networkAdapters;
            // REMOVE ItemSource assignment for cmbModems and cmbDnsServers here.
            // They will be populated directly via their .Items collection in PopulateDropdowns().
            // cmbModems.ItemsSource = _modemIps; // REMOVE THIS LINE
            // cmbDnsServers.ItemsSource = _dnsServers; // REMOVE THIS LINE
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfiguration();
            PopulateNetworkAdapters();
            PopulateDropdowns();

            // Automatically select the first adapter and load its current IP/Subnet
            if (cmbNetworkAdapters.Items.Count > 0)
            {
                cmbNetworkAdapters.SelectedIndex = 0;
                // Manually trigger the selection changed event to load current IP/Subnet
                // Ensure the event handler is wired up (done in PopulateNetworkAdapters)
                cmbNetworkAdapters_SelectionChanged(null, null);
            }
        }

        private void LoadConfiguration()
        {
            string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            _modemIps.Clear();
            _dnsServers.Clear();

            if (!File.Exists(configFilePath))
            {
                ShowStatus("Error: config.txt not found. Please create it in the application directory.", true);
                return;
            }

            string[] lines = File.ReadAllLines(configFilePath);
            string currentSection = string.Empty;

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#")) // Ignore empty lines and comments
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
                    // Check for IPv4 properties
                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    if (ipProps.UnicastAddresses.Any(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                    {
                        _networkAdapters.Add(new NetworkAdapterInfo { Id = ni.Id, Description = ni.Description });
                    }
                }
            }

            if (_networkAdapters.Any())
            {
                // Assign the SelectionChanged event handler
                cmbNetworkAdapters.SelectionChanged += cmbNetworkAdapters_SelectionChanged;
                ShowStatus($"Found {_networkAdapters.Count} active network adapters.", false);
            }
            else
            {
                ShowStatus("No active Ethernet or Wi-Fi adapters found.", true);
            }
        }

        private void PopulateDropdowns()
        {
            // Populate Modem ComboBox
            cmbModems.Items.Clear(); // This is now valid as ItemsSource is not set
            foreach (var modem in _modemIps)
            {
                // The ComboBox will display the key (Modem1, Modem2) but the selected value will be the IP
                ComboBoxItem item = new ComboBoxItem
                {
                    Content = modem.Key + " (" + modem.Value + ")", // Display both name and IP
                    Tag = modem.Value // Store IP in Tag for easy retrieval
                };
                cmbModems.Items.Add(item);
            }
            if (cmbModems.Items.Count > 0) cmbModems.SelectedIndex = 0;

            // Populate DNS ComboBox
            cmbDnsServers.Items.Clear(); // This is now valid as ItemsSource is not set
            foreach (var dns in _dnsServers)
            {
                // Display name and all IPs, store IPs array in Tag
                ComboBoxItem item = new ComboBoxItem
                {
                    Content = dns.Key + " (" + string.Join(", ", dns.Value) + ")",
                    Tag = dns.Value
                };
                cmbDnsServers.Items.Add(item);
            }
            if (cmbDnsServers.Items.Count > 0) cmbDnsServers.SelectedIndex = 0;
        }

        // Event handler for when a network adapter is selected
        private void cmbNetworkAdapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbNetworkAdapters.SelectedItem == null)
            {
                txtStaticIp.Text = "";
                txtSubnetMask.Text = "";
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
                            // Assuming the first IPv4 address and subnet for simplicity in this scenario
                            txtStaticIp.Text = ipAddresses[0];
                            txtSubnetMask.Text = subnetMasks[0];
                            ShowStatus($"Current IP/Subnet loaded for {((NetworkAdapterInfo)cmbNetworkAdapters.SelectedItem).Description}.", false);
                        }
                        else
                        {
                            txtStaticIp.Text = "";
                            txtSubnetMask.Text = "";
                            ShowStatus($"No IPv4 address found for selected adapter, assuming DHCP or unconfigured state. Please manually enter IP/Subnet.", true);
                        }
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Error retrieving current IP/Subnet: {ex.Message}", true);
                txtStaticIp.Text = "";
                txtSubnetMask.Text = "";
            }
        }


        private void ApplySettings_Click(object sender, RoutedEventArgs e)
        {
            if (cmbNetworkAdapters.SelectedItem == null)
            {
                ShowStatus("Please select a network adapter.", true);
                return;
            }

            if (cmbModems.SelectedItem == null)
            {
                ShowStatus("Please select a modem (gateway).", true);
                return;
            }

            if (cmbDnsServers.SelectedItem == null)
            {
                ShowStatus("Please select DNS servers.", true);
                return;
            }

            string adapterId = ((NetworkAdapterInfo)cmbNetworkAdapters.SelectedItem).Id;
            string selectedGateway = ((ComboBoxItem)cmbModems.SelectedItem).Tag.ToString(); // Access Tag from ComboBoxItem
            string[] selectedDns = (string[])((ComboBoxItem)cmbDnsServers.SelectedItem).Tag; // Access Tag from ComboBoxItem

            // Use the IP and Subnet from the TextBoxes, which should already be populated or manually edited
            string currentIp = txtStaticIp.Text.Trim();
            string currentSubnet = txtSubnetMask.Text.Trim();


            // Basic validation
            if (!System.Net.IPAddress.TryParse(currentIp, out _))
            {
                ShowStatus("Invalid Static IP Address format. Please ensure it's correct.", true);
                return;
            }
            if (!System.Net.IPAddress.TryParse(currentSubnet, out _))
            {
                ShowStatus("Invalid Subnet Mask format. Please ensure it's correct.", true);
                return;
            }
            if (!System.Net.IPAddress.TryParse(selectedGateway, out _))
            {
                ShowStatus("Invalid Gateway IP Address format.", true);
                return;
            }
            foreach (string dns in selectedDns)
            {
                if (!System.Net.IPAddress.TryParse(dns, out _))
                {
                    ShowStatus($"Invalid DNS IP Address format: {dns}", true);
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
                        // 1. Set Static IP and Subnet Mask (using current values from TextBoxes)
                        // This step ensures the adapter is configured for static addressing,
                        // which is a prerequisite for setting gateways and DNS.
                        ManagementBaseObject newIP = mo.GetMethodParameters("EnableStatic");
                        newIP["IPAddress"] = new string[] { ipAddress };
                        newIP["SubnetMask"] = new string[] { subnetMask };
                        ManagementBaseObject setIP = mo.InvokeMethod("EnableStatic", newIP, null);

                        if (setIP["ReturnValue"].ToString() != "0")
                        {
                            ShowStatus($"Failed to confirm/set IP Address and Subnet Mask: {GetWmiErrorDescription(setIP["ReturnValue"])}", true);
                            return;
                        }

                        // 2. Set Gateway
                        ManagementBaseObject newGateway = mo.GetMethodParameters("SetGateways");
                        newGateway["DefaultIPGateway"] = new string[] { gateway };
                        newGateway["GatewayCostMetric"] = new int[] { 1 }; // Metric 1 (lowest cost)
                        ManagementBaseObject setGateway = mo.InvokeMethod("SetGateways", newGateway, null);

                        if (setGateway["ReturnValue"].ToString() != "0")
                        {
                            ShowStatus($"Failed to set Gateway: {GetWmiErrorDescription(setGateway["ReturnValue"])}", true);
                            return;
                        }

                        // 3. Set DNS Servers
                        ManagementBaseObject newDNS = mo.GetMethodParameters("SetDNSServerSearchOrder");
                        newDNS["DNSServerSearchOrder"] = dnsServers;
                        ManagementBaseObject setDNS = mo.InvokeMethod("SetDNSServerSearchOrder", newDNS, null);

                        if (setDNS["ReturnValue"].ToString() != "0")
                        {
                            ShowStatus($"Failed to set DNS Servers: {GetWmiErrorDescription(setDNS["ReturnValue"])}", true);
                            return;
                        }

                        ShowStatus($"Successfully applied settings for {((NetworkAdapterInfo)cmbNetworkAdapters.SelectedItem).Description}.", false);
                        ShowStatus($"Current Config: IP: {ipAddress}, Subnet: {subnetMask}, Gateway: {gateway}, DNS: {string.Join(", ", dnsServers)}", false);
                        return; // Found and configured the adapter
                    }
                }
                ShowStatus("Selected network adapter not found or not configured correctly.", true);
            }
            catch (UnauthorizedAccessException)
            {
                ShowStatus("Error: Access Denied. Please run the application as Administrator.", true);
            }
            catch (Exception ex)
            {
                ShowStatus($"An error occurred: {ex.Message}", true);
                // For debugging, consider logging ex.ToString()
            }
        }

        private void ShowStatus(string message, bool isError)
        {
            // Use Dispatcher.Invoke to ensure UI updates are on the main thread
            txtStatus.Dispatcher.Invoke(() =>
            {
                // Prepend new messages to keep the latest at the top
                txtStatus.Text = $"{DateTime.Now:HH:mm:ss} - {(isError ? "ERROR: " : "")}{message}\n" + txtStatus.Text;
                // Set text color based on error status
                if (isError)
                {
                    txtStatus.Foreground = System.Windows.Media.Brushes.Red;
                }
                else
                {
                    // Use a theme-aware brush for normal messages
                    txtStatus.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextFillColorPrimaryBrush");
                }
            });
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
                // You can expand this based on WMI error codes
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