using System.Runtime.InteropServices;
using Baballonia.SDK;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Uvc.Net;

namespace Baballonia.LibuvcCapture;

public sealed class LibuvcCapture : Capture
{
    private const int BSB2E_VENDOR_ID = 0x35bd;
    private const int BSB2E_PRODUCT_ID = 0x0202;
    private const int BSB2E_WIDTH = 800;
    private const int BSB2E_HEIGHT = 400;
    private const int BSB2E_FPS = 90;

    private Context? uvc_context;
    private Device? uvc_device;
    private DeviceHandle? uvc_device_handle;
    private StreamControl stream_control;
    private FrameCallback? frame_callback;
    private readonly CancellationTokenSource update_task_cts = new();

    public LibuvcCapture(string source, ILogger<LibuvcCapture> logger) : base(source, logger)
    {
    }

    public override bool CanConnect(string connectionString)
    {
        return connectionString.StartsWith("/dev/video");
    }

    public override async Task<bool> StartCapture()
    {
        try
        {
            Logger.LogInformation("Starting libuvc capture for BSB2E at {Source}", Source);

            uvc_context = new Context();

            Logger.LogDebug("Searching for device VID={VendorId:X4} PID={ProductId:X4}",
                BSB2E_VENDOR_ID, BSB2E_PRODUCT_ID);

            uvc_device = uvc_context.FindDevice(BSB2E_VENDOR_ID, BSB2E_PRODUCT_ID);

            if (uvc_device == null)
            {
                Logger.LogError("BSB2E device not found");
                return false;
            }

            Logger.LogDebug("Device found, opening...");
            uvc_device_handle = uvc_device.Open();

            Logger.LogDebug("Negotiating stream: {Width}x{Height} @ {Fps}fps MJPEG",
                BSB2E_WIDTH, BSB2E_HEIGHT, BSB2E_FPS);

            try
            {
                uvc_device_handle.GetStreamControlFormatSize(
                    FrameFormat.Mjpeg,
                    BSB2E_WIDTH,
                    BSB2E_HEIGHT,
                    BSB2E_FPS,
                    out stream_control);
            }
            catch
            {
                Logger.LogWarning("Failed to negotiate exact parameters, trying defaults...");
                uvc_device_handle.GetStreamControlFormatSize(
                    FrameFormat.Mjpeg,
                    0,
                    0,
                    0,
                    out stream_control);
            }

            frame_callback = OnFrameReceived;

            Logger.LogDebug("Starting streaming...");
            uvc_device_handle.StartStreaming(ref stream_control, frame_callback);

            IsReady = true;
            Logger.LogInformation("BSB2E streaming started successfully");

            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error starting libuvc capture: {ErrorMessage}", e.Message);
            IsReady = false;
            await StopCapture();
            return false;
        }
    }

    private void OnFrameReceived(ref Frame frame, IntPtr user_ptr)
    {
        if (update_task_cts.Token.IsCancellationRequested)
            return;

        try
        {
            if (frame.FrameFormat != FrameFormat.Mjpeg)
            {
                Logger.LogWarning("Unexpected frame format: {Format}", frame.FrameFormat);
                return;
            }

            if (frame.Data == IntPtr.Zero || frame.DataBytes == IntPtr.Zero)
            {
                Logger.LogWarning("Received empty frame data");
                return;
            }

            int data_size = frame.DataBytes.ToInt32();
            if (data_size <= 0)
            {
                Logger.LogWarning("Invalid frame data size: {Size}", data_size);
                return;
            }

            byte[] jpeg_data = new byte[data_size];
            Marshal.Copy(frame.Data, jpeg_data, 0, data_size);

            Mat decoded_frame = Cv2.ImDecode(jpeg_data, ImreadModes.Color);

            if (decoded_frame.Empty())
            {
                Logger.LogWarning("Failed to decode MJPEG frame");
                return;
            }

            SetRawMat(decoded_frame);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing frame: {ErrorMessage}", e.Message);
        }
    }

    public override Task<bool> StopCapture()
    {
        Logger.LogInformation("Stopping libuvc capture");

        update_task_cts.Cancel();

        try
        {
            if (uvc_device_handle != null)
            {
                try
                {
                    uvc_device_handle.StopStreaming();
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e, "Error stopping streaming: {ErrorMessage}", e.Message);
                }

                uvc_device_handle.Dispose();
                uvc_device_handle = null;
            }

            if (uvc_device != null)
            {
                uvc_device.Dispose();
                uvc_device = null;
            }

            if (uvc_context != null)
            {
                uvc_context.Dispose();
                uvc_context = null;
            }

            frame_callback = null;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error during cleanup: {ErrorMessage}", e.Message);
        }

        IsReady = false;
        Logger.LogInformation("libuvc capture stopped");

        return Task.FromResult(true);
    }

    public override void Dispose()
    {
        StopCapture().Wait();
        update_task_cts.Dispose();
        base.Dispose();
    }
}
