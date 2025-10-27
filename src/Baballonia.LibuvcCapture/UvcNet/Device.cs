using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Uvc.Net
{
    public class Device : IDisposable
    {
        readonly UvcDevice handle;
        readonly ushort vendorId;
        readonly ushort productId;
        readonly ushort complianceLevel;
        readonly string serialNumber;
        readonly string manufacturer;
        readonly string product;

        internal Device(UvcDevice device)
        {
            handle = device;
            IntPtr descriptorPtr;
            var error = NativeMethods.uvc_get_device_descriptor(handle, out descriptorPtr);
            UvcException.ThrowExceptionForUvcError(error);
            try
            {
                UvcDeviceDescriptor descriptor = Marshal.PtrToStructure<UvcDeviceDescriptor>(descriptorPtr);

                vendorId = descriptor.idVendor;
                productId = descriptor.idProduct;
                complianceLevel = descriptor.bcdUVC;

                try
                {
                    serialNumber = (descriptor.serialNumber != IntPtr.Zero) ? Marshal.PtrToStringAnsi(descriptor.serialNumber) ?? string.Empty : string.Empty;
                }
                catch
                {
                    serialNumber = string.Empty;
                }

                try
                {
                    manufacturer = (descriptor.manufacturer != IntPtr.Zero) ? Marshal.PtrToStringAnsi(descriptor.manufacturer) ?? string.Empty : string.Empty;
                }
                catch
                {
                    manufacturer = string.Empty;
                }

                try
                {
                    product = (descriptor.product != IntPtr.Zero) ? Marshal.PtrToStringAnsi(descriptor.product) ?? string.Empty : string.Empty;
                }
                catch
                {
                    product = string.Empty;
                }
            }
            finally { NativeMethods.uvc_free_device_descriptor(descriptorPtr); }
        }

        public ushort VendorId
        {
            get { return vendorId; }
        }

        public ushort ProductId
        {
            get { return productId; }
        }

        public ushort ComplianceLevel
        {
            get { return complianceLevel; }
        }

        public string SerialNumber
        {
            get { return serialNumber; }
        }

        public string Manufacturer
        {
            get { return manufacturer; }
        }

        public string Product
        {
            get { return product; }
        }

        public DeviceHandle Open()
        {
            UvcDeviceHandle devh;
            var error = NativeMethods.uvc_open(handle, out devh);
            UvcException.ThrowExceptionForUvcError(error);
            return new DeviceHandle(devh);
        }

        public void Dispose()
        {
            handle.Dispose();
        }
    }
}
