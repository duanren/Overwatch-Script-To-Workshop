using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.InteropServices;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.Parse;

namespace Deltin.Deltinteger
{
    public class Program
    {
        static Config Config = Config.GetConfig();
        static Log InputLog = new Log("Input");
        static Log Log = new Log(":");

        static readonly CancellationTokenSource CancelSource = new CancellationTokenSource();

        static void Main(string[] args)
        {
            InputSim.ResetAllWindows();
            handler = new ConsoleEventDelegate(ConsoleEventCallback);
            SetConsoleCtrlHandler(handler, true);

            if (args.Length > 0)
            {
                if (File.Exists(args[0]))
                {
#if DEBUG == false
                    try
                    {
#endif
                        Script(args[0]);
#if DEBUG == false
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Internal exception.");
                        Log.Write(ex.ToString());
                    }
#endif
                }
                else
                    Console.WriteLine($"Could not locate file {args[0]}");
            }
            else
            {
                Console.WriteLine("Drag and drop a script over the executable to parse.");
                ConsoleLoop.Start();
            }

            Console.WriteLine("Done. Press enter to exit.");
            Console.ReadLine();
        }

        static void Script(string parseFile)
        {
            string text = File.ReadAllText(parseFile);
            string scriptName = Path.GetFileName(parseFile);

            string compiledDirectory = Path.Combine(Constants.WorkingDirectory, "compiled");

            string compiledName = scriptName + Constants.COMPILED_FILETYPE;

            Rule[] generatedRules = null;
#if DEBUG == false
            try
            {
#endif
                generatedRules = Parser.ParseText(text);
                for (int i = 0; i < generatedRules.Length; i++)
                {
                    Console.WriteLine($"Rule \"{generatedRules[i].Name}\":");
                    generatedRules[i].Print();
                }
#if DEBUG == false
            }
            catch (SyntaxErrorException ex)
            {
                Log.Write(ex.Message, ConsoleColor.DarkRed);
                return;
            }
#endif
            Workshop workshop = new Workshop(generatedRules);
            
            Section();
            if (!Directory.Exists(compiledDirectory))
                Directory.CreateDirectory(compiledDirectory);
            Workshop prev = null;
            if (File.Exists(Path.Combine(compiledDirectory, compiledName)))
            {
                try
                {
                    Stream stream = File.Open(Path.Combine(compiledDirectory, compiledName), FileMode.Open);

                    var formatter = new BinaryFormatter();
                    prev = formatter.Deserialize(stream) as Workshop;

                    stream.Close();

                    Log.Write($"A previously compiled version of \"{scriptName}\" was found.");
                    Log.Write("Rules:");

                    int maxlength = prev.Rules.Length.ToString().Length;
                    for (int i = 0; i < prev.Rules.Length; i++)
                        Log.Colors(new ColorMod($"{i}{new string(' ', maxlength - i.ToString().Length)}", ConsoleColor.Gray), new ColorMod(": " + prev.Rules[i].Name));

                    Log.Write("Press [Y] to update the current workshop ruleset based off the changes since the last compilation. The workshop code must be the same as the rules above.");
                    Log.Write("Press [N] to regenerate the script. This requires the workshop's ruleset to be empty.");
                    if (!YorN())
                        prev = null;
                }
                catch (Exception ex)
                {
                    Log.Write($"Found previous compiled version of \"{scriptName}\", but failed to load it:", ConsoleColor.Black, ConsoleColor.Yellow);
                    Log.Write(ex.ToString());
                }
            }

            List<Rule> previousRules = prev?.Rules.ToList();

            List<int> deleteRules = new List<int>();
            List<RuleAction> ruleActions = new List<RuleAction>();

            // Remove old rules
            if (previousRules != null)
                for (int i = previousRules.Count - 1; i >= 0; i--)
                    if (!generatedRules.Contains(previousRules[i]))
                    {
                        InputLog.Write($"Deleting rule \"{previousRules[i].Name}\"");
                        deleteRules.Add(i);
                        previousRules.RemoveAt(i);
                    }

            int numberOfRules = 0;

            for (int i = 0; i < generatedRules.Length; i++)
            {
                if (previousRules != null && generatedRules[i] == previousRules.ElementAtOrDefault(i))
                    return;

                var previousIndex = previousRules?.IndexOf(generatedRules[i]) ?? -1;

                if (previousIndex == -1)
                {
                    // Create new rule

                    InputLog.Write($"Creating rule \"{generatedRules[i].Name}\"");
                    ruleActions.Add(new RuleAction(generatedRules[i], i, true));
                }
                else if (previousIndex != i)
                {
                    // Move existing rule
                    InputLog.Write($"Moving rule \"{generatedRules[i].Name}\" from #{previousIndex} to #{i}.");
                    ruleActions.Add(new RuleAction(generatedRules[i], previousIndex, i));
                    numberOfRules++;
                }
                else
                {
                    InputLog.Write($"Doing nothing to rule \"{generatedRules[i].Name}\"");
                    ruleActions.Add(new RuleAction(generatedRules[i], i, false));
                    numberOfRules++;
                }
            }

            Log.Write("To setup the input for the generation, leave then re-enter the Settings/Workshop menu in Overwatch.", ConsoleColor.Black, ConsoleColor.Cyan);
            if (prev == null)
                Log.Write("No previous workshop state loaded, make sure that there are no rules in the workshop.");
            Log.Write("It is recommended to save a backup preset before starting the input.", ConsoleColor.Black, ConsoleColor.Yellow);
            Log.Write("If input is incorrect or fails, increase the step wait times in the config.");
            Log.Write("During generation, you can press ctrl+c to cancel.");
            if (Config.StopInput)
                Log.Write("The stopinput option in the config is set to true. During generation any user input sent to the Overwatch window will be ignored." +
                    " After generation if you can't interact with the Overwatch window, start the executable directly then type \"fixinput\".", ConsoleColor.Black, ConsoleColor.Gray);
            Log.Write("Press Enter to start input.");

            List<int> breakAt = null;

            var parameters = Regex.Matches(Console.ReadLine(), @"-([a-z]+)(=(.*))?");
            foreach (Match match in parameters)
            {
                string paramName = match.Groups[1].Value;
                string value = match.Groups.Count == 4 ? match.Groups[3].Value : null;

                List<int> targetRules = null;

                if (value != null)
                {
                    targetRules = new List<int>();
                    string[] rulesToDo = value.Split(',');

                    foreach (string ruleToDo in rulesToDo)
                        if (int.TryParse(ruleToDo, out int set))
                            targetRules.Add(set);
                }

                else if (paramName == "break")
                    breakAt = targetRules;
                else
                    Log.Write($"Unknown parameter {paramName}");
            }

            while ((InputSim.OverwatchProcess = Process.GetProcessesByName("Overwatch").FirstOrDefault()) == null)
            {
                Log.Write("No Overwatch window found, press enter to recheck.");
                Console.ReadLine();
            }

            // Generate rules
            try
            {
                Console.CancelKeyPress += Console_CancelKeyPress;
                InputSim.CancelToken = CancelSource.Token;

                if (Config.StopInput)
                    InputSim.EnableWindow(false);

                InputSim.Press(Keys.Tab, Wait.Short);

                // Delete rules
                int selectedRule = -1;
                foreach (var remove in deleteRules)
                {
                    selectedRule = RuleNav(selectedRule, remove);

                    InputSim.Press(Keys.Space, Wait.Short);
                    InputSim.Press(Keys.Tab, Wait.Short);
                    InputSim.Press(Keys.Right, Wait.Short);
                    InputSim.Press(Keys.Space, Wait.Long);

                    selectedRule = -1;
                }

                // Move and add rules.
                int index = 0;
                foreach (var action in ruleActions)
                {
                    if (breakAt != null && breakAt.Contains(index))
                        Debugger.Break();

                    if (action.RuleActionType == RuleActionType.Add)
                    {
                        selectedRule = ResetRuleNav(selectedRule);

                        action.Rule.Input(numberOfRules, action.RuleIndex);
                        numberOfRules++;
                        action.Exists = true;

                        var conflicting = ruleActions.Where(v => v != null
                        && v.Exists
                        && action.NewIndex <= v.RuleIndex
                        && !ReferenceEquals(action, v));
                        foreach (var conflict in conflicting)
                        {
                            conflict.RuleIndex += 1;
                        }
                    }
                    if (action.RuleIndex != action.NewIndex)
                    {
                        selectedRule = RuleNav(selectedRule, action.RuleIndex);

                        InputSim.Press(Keys.Left, Wait.Short, 2);
                        if (index < action.RuleIndex)
                        {
                            InputSim.Press(Keys.Space, Wait.Short, selectedRule - action.NewIndex);
                        }

                        InputSim.Press(Keys.Right, Wait.Short, 2);

                        selectedRule = index;

                        var conflicting = ruleActions.Where(v => v != null
                        && v.Exists
                        && action.NewIndex <= v.RuleIndex && v.RuleIndex <= action.RuleIndex
                        && !ReferenceEquals(action, v));
                        foreach (var conflict in conflicting)
                        {
                            conflict.RuleIndex += 1;
                        }

                        action.RuleIndex = action.NewIndex;
                    }

                    index++;
                }

                selectedRule = ResetRuleNav(selectedRule);

                InputSim.EnableWindow(true);

                Log.Write("Input completed.");
                Log.Write("Press [Y] to save the current workshop state. Only do this if the input was sucessful.");
                Log.Write("Press [N] to discard the saved workshop state. The next generation will requre the workshop state to be the same as before the last input.");
                if (YorN())
                {
                    Stream saveStream = File.Open(Path.Combine(compiledDirectory, compiledName), FileMode.Create);

                    var saveFormatter = new BinaryFormatter();
                    saveFormatter.Serialize(saveStream, workshop);

                    saveStream.Close();
                }
            }
            catch (OperationCanceledException)
            {
                Log.Write("Generation canceled.");
            }
            finally
            {
                InputSim.EnableWindow(true);
            }
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            CancelSource.Cancel();
        }

        static int RuleNav(int selectedRule, int toRule)
        {
            if (selectedRule == -1)
            {
                InputSim.Press(Keys.Down, Wait.Long);
                InputSim.Press(Keys.Left, Wait.Long);
                selectedRule = 0;
            }

            if (selectedRule < toRule)
                InputSim.Press(Keys.Down, Wait.Short, toRule - selectedRule);
            else if (selectedRule > toRule)
                InputSim.Press(Keys.Up, Wait.Short, selectedRule - toRule);

            return toRule;
        }

        static int ResetRuleNav(int selectedRule)
        {
            if (selectedRule != -1)
                InputSim.Press(Keys.Up, Wait.Short, selectedRule + 1);

            return -1;
        }

        static bool YorN()
        {
            while (true)
            {
                var key = Console.ReadKey();
                if (key.Key == ConsoleKey.Y)
                {
                    Console.WriteLine();
                    return true;
                }
                else if (key.Key == ConsoleKey.N)
                {
                    Console.WriteLine();
                    return false;
                }
            }
        }

        static void Section()
        {
            Console.WriteLine("\n---");
        }

        static bool ConsoleEventCallback(int eventType)
        {
            if (eventType == 2)
            {
                InputSim.ResetAllWindows();
            }
            return false;
        }
        static ConsoleEventDelegate handler;   // Keeps it from getting garbage collected
                                               // Pinvoke
        private delegate bool ConsoleEventDelegate(int eventType);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate callback, bool add);
    }

    [Serializable]
    class Workshop : IEquatable<Workshop>
    {
        public Workshop(Rule[] rules)
        {
            Rules = rules;
        }
        public Rule[] Rules { get; set; }

        public bool Equals(Workshop other)
        {
            if (other == null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            if (other.Rules.Length != Rules.Length)
                return false;

            for (int i = 0; i < Rules.Length; i++)
                if (!Rules[i].Equals(other.Rules[i]))
                    return false;

            return true;
        }
    }

    class RuleAction
    {
        public RuleAction(Rule rule, int ruleIndex, bool create)
        {
            Rule = rule;
            RuleIndex = ruleIndex;
            NewIndex = ruleIndex;

            if (create)
            {
                RuleActionType = RuleActionType.Add;
                Exists = false;
            }
            else
            {
                RuleActionType = RuleActionType.None;
                Exists = true;
            }
        }

        public RuleAction(Rule rule, int ruleIndex, int newIndex)
        {
            Rule = rule;
            RuleIndex = ruleIndex;
            NewIndex = newIndex;
            RuleActionType = RuleActionType.Move;
            Exists = true;
        }

        public Rule Rule { get; private set; }

        public int RuleIndex { get; set; }
        public int NewIndex { get; set; }

        public bool Exists { get; set; }

        public RuleActionType RuleActionType { get; private set; }
    }

    enum RuleActionType
    {
        None,
        Add,
        Move,
    }
}
