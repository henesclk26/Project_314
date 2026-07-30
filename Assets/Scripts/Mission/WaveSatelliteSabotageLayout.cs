using System;
using System.Collections.Generic;

public static class WaveSatelliteSabotageLayout
{
    public const int SatelliteCount = 6;
    public const int EmptyPort = 0xF;
    public const ulong EmptyConnections = 0xFFFFFFUL;

    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    public sealed class Layout
    {
        public string[] Codes;
        public int[] TargetOrder;
    }

    public static Layout Create(int seed)
    {
        Random random = new Random(seed);
        string[] codes = new string[SatelliteCount];
        HashSet<string> usedCodes = new HashSet<string>();

        for (int i = 0; i < SatelliteCount; i++)
        {
            string code;
            do
            {
                code = CreateCode(random);
            }
            while (!usedCodes.Add(code));

            codes[i] = code;
        }

        int[] targetOrder = { 0, 1, 2, 3, 4, 5 };
        for (int i = targetOrder.Length - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            (targetOrder[i], targetOrder[swapIndex]) =
                (targetOrder[swapIndex], targetOrder[i]);
        }

        return new Layout
        {
            Codes = codes,
            TargetOrder = targetOrder
        };
    }

    public static int GetSatelliteAtPort(ulong packedConnections, int portIndex)
    {
        if (portIndex < 0 || portIndex >= SatelliteCount)
            return EmptyPort;

        return (int)((packedConnections >> (portIndex * 4)) & 0xFUL);
    }

    public static int FindSatellitePort(ulong packedConnections, int satelliteIndex)
    {
        if (satelliteIndex < 0 || satelliteIndex >= SatelliteCount)
            return -1;

        for (int port = 0; port < SatelliteCount; port++)
        {
            if (GetSatelliteAtPort(packedConnections, port) == satelliteIndex)
                return port;
        }

        return -1;
    }

    public static ulong ConnectToFirstEmptyPort(
        ulong packedConnections,
        int satelliteIndex,
        out int assignedPort)
    {
        assignedPort = -1;
        if (satelliteIndex < 0 ||
            satelliteIndex >= SatelliteCount ||
            FindSatellitePort(packedConnections, satelliteIndex) >= 0)
        {
            return packedConnections;
        }

        for (int port = 0; port < SatelliteCount; port++)
        {
            if (GetSatelliteAtPort(packedConnections, port) != EmptyPort)
                continue;

            assignedPort = port;
            return SetPort(packedConnections, port, satelliteIndex);
        }

        return packedConnections;
    }

    public static ulong DisconnectSatellite(
        ulong packedConnections,
        int satelliteIndex,
        out int disconnectedPort)
    {
        disconnectedPort = FindSatellitePort(packedConnections, satelliteIndex);
        if (disconnectedPort < 0)
            return packedConnections;

        return SetPort(packedConnections, disconnectedPort, EmptyPort);
    }

    public static int GetConnectedCount(ulong packedConnections)
    {
        int count = 0;
        for (int port = 0; port < SatelliteCount; port++)
        {
            if (GetSatelliteAtPort(packedConnections, port) != EmptyPort)
                count++;
        }
        return count;
    }

    public static bool IsComplete(Layout layout, ulong packedConnections)
    {
        if (layout == null || layout.TargetOrder == null ||
            layout.TargetOrder.Length != SatelliteCount)
        {
            return false;
        }

        for (int port = 0; port < SatelliteCount; port++)
        {
            if (GetSatelliteAtPort(packedConnections, port) !=
                layout.TargetOrder[port])
            {
                return false;
            }
        }

        return true;
    }

    private static string CreateCode(Random random)
    {
        char[] characters =
        {
            Letters[random.Next(Letters.Length)],
            Letters[random.Next(Letters.Length)],
            Digits[random.Next(Digits.Length)],
            Digits[random.Next(Digits.Length)],
            Digits[random.Next(Digits.Length)]
        };

        for (int i = characters.Length - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            (characters[i], characters[swapIndex]) =
                (characters[swapIndex], characters[i]);
        }

        return new string(characters);
    }

    private static ulong SetPort(
        ulong packedConnections,
        int portIndex,
        int satelliteIndex)
    {
        int shift = portIndex * 4;
        ulong clearMask = ~(0xFUL << shift);
        return (packedConnections & clearMask) |
               ((ulong)(satelliteIndex & 0xF) << shift);
    }
}
