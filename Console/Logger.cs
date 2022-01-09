﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using GameServer.Console.Commands;

namespace GameServer.Console
{
    public class Logger
    {
        // Commands
        public static Dictionary<string, Command> Commands { get; set; }

        // Command History
        private static List<string> CommandHistory { get; set; }

        // Text Field
        public static LoggerTextField TextField { get; set; }
        private static object ThreadLock { get; set; }

        // Logging
        private static ConcurrentQueue<LoggerMessage> Messages { get; set; }
        public static int LoggerMessageRow { get; set; }

        public static void LogError(object obj) 
        {
            Log(obj, "ERROR", "&9");
        }

        public static void LogWarning(object obj)
        {
            Log(obj, "WARN", "&y");
        }

        public static void LogRaw(object obj, string color = "&r") 
        {
            var str = $"{color}{obj}";

            Messages.Enqueue(new LoggerMessage(str));
        }

        public static void Log(object obj, string logLevel = "INFO", string color = "&r")
        {
            var time = $"{DateTime.Now:HH:mm:ss}";
            var thread = Thread.CurrentThread.Name;
            var str = $"{color}{time} [{thread}] [{logLevel}] {obj}";

            Messages.Enqueue(new LoggerMessage(str));
        }

        public static void MessagesThread()
        {
            Thread.CurrentThread.Name = "CONSOLE";

            Messages = new();
            ThreadLock = new();

            var messageThreadTickRate = 200;

            while (true)
            {
                Thread.Sleep(messageThreadTickRate); // Otherwise CPU resources will go to waste

                lock (ThreadLock) 
                {
                    while (Messages.TryDequeue(out LoggerMessage message))
                    {
                        TextField.MoveDown();
                        AddMessageToConsole(message);
                    }
                }
            }
        }

        public static void InputThread()
        {
            Thread.CurrentThread.Name = "CONSOLE";

            Commands = typeof(Command).Assembly.GetTypes().Where(x => typeof(Command).IsAssignableFrom(x) && !x.IsAbstract).Select(Activator.CreateInstance).Cast<Command>()
                .ToDictionary(x => x.GetType().Name.Replace(typeof(Command).Name, "").ToLower(), x => x);

            CommandHistory = new();
            TextField = new();

#if Windows
            Terminal.DisableMinimize();
            Terminal.DisableMaximize();
            Terminal.DisableResize();
            Terminal.DisableConsoleFeatures();
            Terminal.DisableAbruptExit();
#endif

            TextField.row = 1; // Keep the text field 1 row ahead of the logged messages

            var spaceBarCount = 0;
            var commandHistoryIndex = 0;

            while (true)
            {
                lock (ThreadLock)
                    System.Console.CursorTop = TextField.row;

                var keyInfo = System.Console.ReadKey(true);

                lock (ThreadLock) 
                {
                    if (keyInfo.Key == ConsoleKey.Spacebar) 
                        spaceBarCount++;

                    if (keyInfo.Key == ConsoleKey.Delete) 
                    {
                        var input = TextField.input;

                        if (input == "" || System.Console.CursorLeft == input.Length)
                            continue;

                        System.Console.Write(' ');
                        System.Console.CursorLeft--;

                        // Update the input variable
                        
                        var cursorColumn = input.Length - 1 - System.Console.CursorLeft;
                        TextField.input = input.Remove(input.Length - 1 - cursorColumn, 1);

                        // Since the input was edited, it needs to be redrawn
                        TextField.Redraw();

                        continue;
                    }

                    if (keyInfo.Key == ConsoleKey.Backspace) 
                    {
                        // Stay within the console window bounds
                        if (System.Console.CursorLeft <= 0)
                            continue;

                        // Delete the character and move back one space
                        System.Console.CursorLeft--;
                        System.Console.Write(' ');
                        System.Console.CursorLeft--;

                        // Update the input variable
                        var input = TextField.input;
                        var cursorColumn = input.Length - 1 - System.Console.CursorLeft;
                        TextField.input = input.Remove(input.Length - 1 - cursorColumn, 1);

                        // Since the input was edited, it needs to be redrawn
                        TextField.Redraw();
                        continue;
                    }

                    if (keyInfo.Key == ConsoleKey.LeftArrow) 
                    {
                        // Stay within the console window bounds
                        if (System.Console.CursorLeft <= 0)
                            continue;

                        System.Console.CursorLeft--;
                        continue;
                    }

                    if (keyInfo.Key == ConsoleKey.RightArrow)
                    {
                        // Stay within the console window bounds
                        if (System.Console.CursorLeft >= System.Console.WindowWidth - 1)
                            continue;

                        System.Console.CursorLeft++;
                        continue;
                    }

                    if (keyInfo.Key == ConsoleKey.DownArrow)
                    {
                        if (CommandHistory.Count < 1)
                            continue;

                        if (commandHistoryIndex <= 1)
                            continue;

                        commandHistoryIndex--;
                        var nextCommand = CommandHistory[^commandHistoryIndex]; // CommandHistory.Count - commandHistoryIndex

                        TextField.input = nextCommand;
                        TextField.Clear(false);

                        System.Console.WriteLine(nextCommand);

                        // CursorLeft should be at the very end of the text
                        System.Console.CursorLeft = nextCommand.Length;

                        continue;
                    }

                    if (keyInfo.Key == ConsoleKey.UpArrow)
                    {
                        if (CommandHistory.Count < 1)
                            continue;

                        if (commandHistoryIndex >= CommandHistory.Count)
                            continue;

                        var prevCommand = CommandHistory[CommandHistory.Count - 1 - commandHistoryIndex];
                        commandHistoryIndex++;

                        TextField.input = prevCommand;
                        TextField.Clear(false);

                        System.Console.WriteLine(prevCommand);

                        // CursorLeft should be at the very end of the text
                        System.Console.CursorLeft = prevCommand.Length;

                        continue;
                    }

                    if (keyInfo.Key == ConsoleKey.Enter) 
                    {
                        if (TextField.input == null)
                            continue;

                        var inputArr = TextField.input.Trim().ToLower().Split(' ');
                        var cmd = inputArr[0];
                        var args = inputArr.Skip(1).ToArray();

                        // If the user spams spacebar but the cmd is still empty, reset the user input if enter is pressed
                        if (cmd == "" && spaceBarCount > 0) 
                        {
                            spaceBarCount = 0;
                            System.Console.CursorLeft = 0;
                            TextField.column = 0;
                            TextField.input = "";
                            continue;
                        }

                        // Do not do anything if command string is empty
                        if (cmd.Equals(""))
                            continue;

                        if (Commands.ContainsKey(cmd))
                            Commands[cmd].Run(args);
                        else 
                        {
                            var foundCmd = false;
                            foreach (var command in Commands.Values) 
                            {
                                if (command.Aliases == null)
                                    continue;

                                foreach (var alias in command.Aliases) 
                                {
                                    if (cmd.Equals(alias)) 
                                    {
                                        foundCmd = true;
                                        command.Run(args);
                                    }
                                }
                            }

                            if (!foundCmd)
                                Log($"Unknown Command: '{cmd}'");
                        }   

                        // Only keep track of a set of previously entered commands
                        var maxHistoryCommands = 255;
                        if (CommandHistory.Count > maxHistoryCommands) 
                            CommandHistory.RemoveAt(0);

                        // Add command along with args[] to command history
                        CommandHistory.Add(TextField.input.Trim().ToLower().Split(' ')[0]);

                        // Reset command history index to 0 on enter
                        commandHistoryIndex = 0;

                        // Reset input and text field input
                        TextField.Clear(true);
                        spaceBarCount = 0;
                        continue;
                    }

                    // Write the character to the console and input, also keep track of text field column
                    System.Console.Write(keyInfo.KeyChar);

                    TextField.input += keyInfo.KeyChar;
                    TextField.column++;

                    if (System.Console.CursorLeft >= System.Console.WindowWidth - 1) 
                    {
                        // TODO: Make input text field scroll horizontally
                    }
                }
            }
        }

        private static void AddMessageToConsole(LoggerMessage message) 
        {
            // Set the cursor to the logger area
            var prevTextFieldColumn = System.Console.CursorLeft;

            // Add the message to the logger area
            WriteColoredMessage(message.text);

            // Reset cursor position to the text field area
            System.Console.CursorTop++; // Move down more to get back to text field input

            // Calculate the number of lines needed from this message
            var lines = message.GetLines();

            // Move the logger area and text field area down by 'lines'
            LoggerMessageRow += lines;
            TextField.row += lines;

            // Reset text field column
            TextField.column = 0;

            // Put cursor left back to where it previously was
            System.Console.CursorLeft = prevTextFieldColumn;
        }

        private static void WriteColoredMessage(string message) 
        {
            System.Console.CursorLeft = 0;
            System.Console.CursorTop = LoggerMessageRow;

            // Check for color codes in the message
            if (message.Contains('&'))
            {
                // The beginning of the message
                var noColorEnd = message.IndexOf('&');
                var beginningNoColorMessage = message.Substring(0, noColorEnd);

                // The rest of the message
                var remainingMessage = message[noColorEnd..];
                var remainingWords = remainingMessage.Split('&');

                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write(beginningNoColorMessage);

                foreach (var word in remainingWords)
                {
                    if (word == "")
                        continue;

                    // Check for valid int color code after &
                    if (int.TryParse(word[0..1], out int intColorCode))
                    {
                        System.Console.ForegroundColor = LoggerColor.numColorCodes[intColorCode];
                        System.Console.Write(word[1..]);
                        ResetColor();
                        continue;
                    }

                    // Check for valid char color code after &
                    var charColorCode = char.Parse(word[0..1]);

                    if (charColorCode == 'r')
                    {
                        ResetColor();
                        System.Console.Write(word[1..]);
                        continue;
                    }

                    var foundCharColorCode = false;

                    foreach (var entry in LoggerColor.charColorCodes)
                    {
                        if (charColorCode == entry.Key)
                        {
                            System.Console.ForegroundColor = LoggerColor.charColorCodes[charColorCode];
                            System.Console.Write(word[1..]);
                            ResetColor();
                            foundCharColorCode = true;
                            break;
                        }
                    }

                    // Did not find any color codes
                    if (!foundCharColorCode)
                    {
                        System.Console.Write(word);
                        ResetColor(); // Just for added measure
                    }
                }
            }
            else
            {
                // There are no color codes in the message, simply write it to the console
                System.Console.Write(message);
            }

            System.Console.Write(Environment.NewLine);
        }

        private static void ResetColor() 
        {
            System.Console.BackgroundColor = ConsoleColor.Black;
            System.Console.ForegroundColor = ConsoleColor.White;
        }

        // TEST MESSAGE (2 lines)
        // TheredfoxjumpedoverthefenceTheredfoxjumpedoverthefenceTheredfoxjumpedoverthefenceTheredfoxjumpedoverthefenceTheredfoxjumpedoverthefenceTheredfoxjumpedoverthefenceTheredfoxjumpedoverthefenceTheredfoxjumpedoverthefenceTheredfoxjumpedoverthefence
    }
}
