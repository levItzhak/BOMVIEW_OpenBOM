using System.Net.NetworkInformation;
using System.Windows;

namespace BOMVIEW
{
    public static class NetworkHelper
    {
        public static bool CheckInternetWithMessage()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var result = ping.Send("8.8.8.8", 2000);
                    if (result?.Status != IPStatus.Success)
                    {
                        MessageBox.Show("No internet connection available.",
                                      "Network Error",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Warning);
                        return false;
                    }
                    return true;
                }
            }
            catch
            {
                MessageBox.Show("No internet connection available.",
                              "Network Error",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
                return false;
            }
        }
    }
}