using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Baballonia.SDK;
using Microsoft.Extensions.Logging;

namespace Baballonia.LibuvcCapture;

public class LibuvcCaptureFactory : ICaptureFactory
{
    private readonly ILoggerFactory logger_factory;
    private readonly ILogger<LibuvcCaptureFactory> logger;
    private const int BSB2E_VENDOR_ID = 0x35bd;
    private const int BSB2E_PRODUCT_ID = 0x0202;

    public LibuvcCaptureFactory(ILoggerFactory loggerFactory)
    {
        logger_factory = loggerFactory;
        logger = loggerFactory.CreateLogger<LibuvcCaptureFactory>();
        logger.LogInformation("LibuvcCaptureFactory initialized");
    }

    public Capture Create(string address)
    {
        return new LibuvcCapture(address, logger_factory.CreateLogger<LibuvcCapture>());
    }

    public bool CanConnect(string address)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        // Extract device path from either "/dev/video0" or "Friendly Name (video0)"
        string devicePath;
        if (address.StartsWith("/dev/video"))
        {
            devicePath = address;
        }
        else if (address.Contains("video") && address.Contains("(") && address.Contains(")"))
        {
            // Extract video device from friendly name like "Bigscreen Bigeye (video0)"
            var match = System.Text.RegularExpressions.Regex.Match(address, @"\(video(\d+)\)");
            if (match.Success)
            {
                devicePath = $"/dev/video{match.Groups[1].Value}";
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        // Check if this is a BSB2E device
        return IsBigscreenBeyond2E(devicePath);
    }

    public string GetProviderName()
    {
        return nameof(LibuvcCapture);
    }

    [SupportedOSPlatform("linux")]
    private bool IsBigscreenBeyond2E(string devicePath)
    {
        [DllImport("libudev.so")] static extern IntPtr udev_new();
        [DllImport("libudev.so")] static extern IntPtr udev_unref(IntPtr udev);
        [DllImport("libudev.so")] static extern IntPtr udev_enumerate_new(IntPtr udev);
        [DllImport("libudev.so")] static extern int udev_enumerate_add_match_subsystem(IntPtr udevEnumerate, [MarshalAs(UnmanagedType.LPUTF8Str)] string subsystem);
        [DllImport("libudev.so")] static extern int udev_enumerate_scan_devices(IntPtr udevEnumerate);
        [DllImport("libudev.so")] static extern IntPtr udev_enumerate_get_list_entry(IntPtr udevEnumerate);
        [DllImport("libudev.so")] static extern IntPtr udev_list_entry_get_next(IntPtr listEntry);
        [DllImport("libudev.so")] static extern IntPtr udev_list_entry_get_name(IntPtr listEntry);
        [DllImport("libudev.so")] static extern IntPtr udev_device_new_from_syspath(IntPtr udev, IntPtr syspath);
        [DllImport("libudev.so")] static extern IntPtr udev_device_get_devnode(IntPtr udevDevice);
        [DllImport("libudev.so")] static extern IntPtr udev_device_get_property_value(IntPtr udevDevice, [MarshalAs(UnmanagedType.LPUTF8Str)] string property);
        [DllImport("libudev.so")] static extern IntPtr udev_enumerate_unref(IntPtr udevEnumerate);
        [DllImport("libudev.so")] static extern IntPtr udev_device_unref(IntPtr udevDevice);

        try
        {
            IntPtr udev = udev_new();
            if (udev == IntPtr.Zero)
                return false;

            IntPtr enumerate = udev_enumerate_new(udev);
            if (enumerate == IntPtr.Zero)
            {
                udev_unref(udev);
                return false;
            }

            try
            {
                udev_enumerate_add_match_subsystem(enumerate, "video4linux");
                udev_enumerate_scan_devices(enumerate);

                for (IntPtr iter = udev_enumerate_get_list_entry(enumerate);
                     iter != IntPtr.Zero;
                     iter = udev_list_entry_get_next(iter))
                {
                    IntPtr syspath = udev_list_entry_get_name(iter);
                    IntPtr udev_device = udev_device_new_from_syspath(udev, syspath);
                    if (udev_device == IntPtr.Zero)
                        continue;

                    try
                    {
                        IntPtr devnode = udev_device_get_devnode(udev_device);
                        if (devnode == IntPtr.Zero)
                            continue;

                        string? current_device_path = Marshal.PtrToStringUTF8(devnode);
                        if (current_device_path != devicePath)
                            continue;

                        IntPtr vendor_id_ptr = udev_device_get_property_value(udev_device, "ID_VENDOR_ID");
                        IntPtr product_id_ptr = udev_device_get_property_value(udev_device, "ID_MODEL_ID");

                        if (vendor_id_ptr == IntPtr.Zero || product_id_ptr == IntPtr.Zero)
                            return false;

                        string? vendor_id_str = Marshal.PtrToStringUTF8(vendor_id_ptr);
                        string? product_id_str = Marshal.PtrToStringUTF8(product_id_ptr);

                        if (vendor_id_str == null || product_id_str == null)
                            return false;

                        if (int.TryParse(vendor_id_str, System.Globalization.NumberStyles.HexNumber, null, out int vendor_id) &&
                            int.TryParse(product_id_str, System.Globalization.NumberStyles.HexNumber, null, out int product_id))
                        {
                            return vendor_id == BSB2E_VENDOR_ID && product_id == BSB2E_PRODUCT_ID;
                        }

                        return false;
                    }
                    finally
                    {
                        udev_device_unref(udev_device);
                    }
                }

                return false;
            }
            finally
            {
                udev_enumerate_unref(enumerate);
                udev_unref(udev);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if {DevicePath} is BSB2E", devicePath);
            return false;
        }
    }
}
