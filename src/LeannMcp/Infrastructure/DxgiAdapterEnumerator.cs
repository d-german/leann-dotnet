using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Infrastructure;

/// <summary>
/// Enumerates DXGI adapters to discover available GPUs and their capabilities.
/// Used to auto-select the best GPU for DirectML (discrete > integrated).
/// </summary>
internal static class DxgiAdapterEnumerator
{
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    // IDXGIFactory1::EnumAdapters1 is vtable slot 12
    private const int EnumAdapters1VtableSlot = 12;
    // IDXGIAdapter1::GetDesc1 is vtable slot 10
    private const int GetDesc1VtableSlot = 10;

    internal readonly record struct AdapterInfo(
        int Index,
        string Description,
        uint VendorId,
        ulong DedicatedVideoMemory,
        ulong DedicatedSystemMemory,
        ulong SharedSystemMemory,
        bool IsDiscrete);

    /// <summary>
    /// Enumerates all DXGI adapters on the system. Returns an empty list on failure.
    /// </summary>
    internal static List<AdapterInfo> EnumerateAdapters(ILogger? logger = null)
    {
        var adapters = new List<AdapterInfo>();

        var factoryGuid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
        var hr = CreateDXGIFactory1(ref factoryGuid, out var factoryPtr);
        if (hr < 0)
        {
            logger?.LogDebug("CreateDXGIFactory1 failed with HRESULT {Hr:X8}", hr);
            return adapters;
        }

        try
        {
            var factoryVtable = Marshal.ReadIntPtr(factoryPtr);
            var enumAdaptersFunc = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(
                Marshal.ReadIntPtr(factoryVtable, EnumAdapters1VtableSlot * IntPtr.Size));

            for (var i = 0u; ; i++)
            {
                hr = enumAdaptersFunc(factoryPtr, i, out var adapterPtr);
                if (hr < 0) break; // DXGI_ERROR_NOT_FOUND = end of list

                try
                {
                    var adapterVtable = Marshal.ReadIntPtr(adapterPtr);
                    var getDescFunc = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(
                        Marshal.ReadIntPtr(adapterVtable, GetDesc1VtableSlot * IntPtr.Size));

                    var desc = new DxgiAdapterDesc1();
                    hr = getDescFunc(adapterPtr, ref desc);
                    if (hr >= 0)
                    {
                        var description = new string(desc.Description).TrimEnd('\0');
                        var isDiscrete = desc.DedicatedVideoMemory > 512 * 1024 * 1024UL // >512 MB VRAM
                                         && (desc.Flags & 0x2) == 0; // not DXGI_ADAPTER_FLAG_SOFTWARE

                        adapters.Add(new AdapterInfo(
                            (int)i,
                            description,
                            desc.VendorId,
                            desc.DedicatedVideoMemory,
                            desc.DedicatedSystemMemory,
                            desc.SharedSystemMemory,
                            isDiscrete));
                    }
                }
                finally
                {
                    Marshal.Release(adapterPtr);
                }
            }
        }
        finally
        {
            Marshal.Release(factoryPtr);
        }

        return adapters;
    }

    /// <summary>
    /// Selects the best adapter index for GPU compute.
    /// Prefers discrete GPUs (highest dedicated VRAM). Falls back to adapter 0.
    /// </summary>
    internal static int SelectBestAdapter(List<AdapterInfo> adapters, ILogger? logger = null)
    {
        if (adapters.Count == 0) return 0;

        // Log all adapters
        foreach (var adapter in adapters)
        {
            var vramMb = adapter.DedicatedVideoMemory / (1024 * 1024);
            logger?.LogInformation(
                "  GPU [{Index}]: {Description} — {VramMb} MB VRAM{Discrete}",
                adapter.Index, adapter.Description, vramMb,
                adapter.IsDiscrete ? " (discrete)" : "");
        }

        // Prefer discrete adapters, then sort by dedicated VRAM descending
        var best = adapters
            .OrderByDescending(a => a.IsDiscrete)
            .ThenByDescending(a => a.DedicatedVideoMemory)
            .First();

        return best.Index;
    }

    // COM delegates
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr factory, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr adapter, ref DxgiAdapterDesc1 desc);

    // DXGI_ADAPTER_DESC1 layout
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public char[] Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long Luid;
        public uint Flags;
    }
}
