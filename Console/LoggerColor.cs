﻿using System;
using System.Collections.Generic;

namespace GameServer.Console
{
    public static class LoggerColor
    {
        public static readonly ConsoleColor[] numColorCodes = new ConsoleColor[10] // there can only be up to 10 num color codes [0..9]
        {
            ConsoleColor.Black,        // 0
            ConsoleColor.DarkGray,     // 1 (ConsoleColor.DarkGray and ConsoleColor.Gray seem to be the same color)
            ConsoleColor.Gray,         // 2 ('g' conflicts with Green color)
            ConsoleColor.DarkMagenta,  // 3
            ConsoleColor.DarkBlue,     // 4
            ConsoleColor.DarkCyan,     // 5
            ConsoleColor.DarkGreen,    // 6
            ConsoleColor.DarkYellow,   // 7
            ConsoleColor.DarkRed,      // 8
            ConsoleColor.Red           // 9 ('r' is reserved for resetting the colors, 'r' also conflicts with Red color)
        };

        public static readonly Dictionary<char, ConsoleColor> charColorCodes = new()
        {
            { 'b', ConsoleColor.Blue },
            { 'c', ConsoleColor.Cyan },
            { 'g', ConsoleColor.Green },
            { 'm', ConsoleColor.Magenta },
            { 'y', ConsoleColor.Yellow }
        };
    }
}
