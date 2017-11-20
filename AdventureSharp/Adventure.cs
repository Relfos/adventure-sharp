using System;
using System.Collections.Generic;
using System.Text;

using LunarParser;
using LunarParser.XML;
using System.IO;

namespace AdventureSharp
{
    public abstract class Driver
    {
        public abstract void WriteLine(string s);
        public abstract void Prompt();
        public abstract string ReadLine();
    }

    public class Character
    {
        public class Stat
        {
            public enum Kind
            {
                Skill,
                Stamina,
                Luck,
                Magic,
                Armor,
                Fear
            }

            public int current;
            public int minimum;
            public int maximum;
        }

        public Dictionary<Stat.Kind, Stat> stats = new Dictionary<Stat.Kind, Stat>();
    }

#region COMMANDS
    public abstract class Command
    {
        public abstract bool Execute(Adventure adventure);

        public static Command FromNode(DataNode node)
        {
            var tag = node.Name.ToLowerInvariant();
            switch (tag)
            {
                case "text":
                    return new TextCommand(node);

                case "enter":
                    return new EnterCommand(node.GetString("area"));
            }

            return null;
        }

        public virtual Command ReceiveInput(string s)
        {
            return null;
        }
    }

    public class TextCommand: Command
    {
        public string text;

        public TextCommand(DataNode src)
        {
            this.text = src.Value;
        }

        public override bool Execute(Adventure adventure)
        {
            adventure.driver.WriteLine(this.text);
            return true;
        }
    }

    public class EnterCommand : Command
    {
        public string id;

        public EnterCommand(string id)
        {
            this.id = id;
        }

        public override bool Execute(Adventure adventure)
        {
            adventure.state.area = adventure.FindArea(this.id);
            return true;
        }
    }

    public class ContinueCommand : Command
    {
        public override bool Execute(Adventure adventure)
        {
            return true;
        }
    }

    public class CustomCommand : Command
    {
        public Func<Adventure, bool> action;

        public CustomCommand(Func<Adventure, bool> action)
        {
            this.action = action;
        }

        public override bool Execute(Adventure adventure)
        {
            return action(adventure);
        }
    }

    public class SelectCommand : Command
    {
        public struct Option
        {
            public string text;
            public Command command;
        }

        public string text;
        public List<Option> options;

        public SelectCommand(DataNode src)
        {
            this.text = src.Value;

            this.options = new List<Option>();
        }

        public SelectCommand(string text, List<Option> options)
        {
            this.text = text;
            this.options = options;
        }

        public override bool Execute(Adventure adventure)
        {
            adventure.driver.WriteLine(text);
            for (int i=1; i<= options.Count; i++)
            {
                adventure.driver.WriteLine($"{i} - {options[i-1].text}");
            }
            return false;
        }

        public override Command ReceiveInput(string s)
        {
            for (int i = 1; i <= options.Count; i++)
            {
                var option = options[i - 1];
                if (s.Equals(i.ToString()) || s.Equals(option.text))
                {
                    return option.command;
                }
            }

            return null;
        }
    }
    #endregion

    public class Adventure
    {
        public Driver driver { get; private set; }
        public State state { get; private set; }

        public abstract class Target
        {
            public string name;
            public string desc;
        }

        public class Item: Target
        {
            public string id;

            public Item(DataNode node)
            {
                this.id = node.GetString("id");
                this.name = node.GetString("name");
                this.desc = node.GetString("desc");
            }

            public virtual bool Execute(Adventure adventure)
            {
                adventure.driver.WriteLine("Nothing happened...");
                return false;
            }
        }

        public class ItemContainer
        {
            public Dictionary<Item, int> entries = new Dictionary<Item, int>();

            public void Move(Item item, ItemContainer other, int ammount = 1)
            {
                ammount = Remove(item, ammount);
                other.Add(item, ammount);
            }

            public void Add(Item item, int ammount = 1)
            {
                if (ammount<=0)
                {
                    return;
                }

                if (entries.ContainsKey(item))
                {
                    entries[item]+=ammount;
                }
                else
                {
                    entries[item] = ammount;
                }
            }

            public int Remove(Item item, int ammount = 1)
            {
                if (entries.ContainsKey(item))
                {
                    if (entries[item] > ammount)
                    {
                        entries[item]-=ammount;
                    }
                    else
                    {
                        ammount = entries[item];
                        entries.Remove(item);
                    }

                    return ammount;
                }

                return 0;
            }
        }

        public class Area
        {
            public class Connection
            {
                public Area area;
                public string direction;
                public string type;

                public bool open;

                public Connection(Adventure adventure, DataNode node)
                {
                    var area_id = node.GetString("to");
                    this.area = adventure.FindArea(area_id);
                    this.direction = node.GetString("dir");
                    this.type = node.GetString("type");

                    this.open = this.type == "passage";
                }
            }

            public class Prop: Target
            {
                public ItemContainer items = new ItemContainer();

                public Prop(Adventure adventure, DataNode node)
                {
                    this.name = node.GetString("name");
                    this.desc = node.GetString("desc");

                    foreach (var child in node.Children)
                    {
                        if (child.Name == "contains")
                        {
                            var ammount = child.GetInt32("ammount", 1);
                            var item_id = child.GetString("item");

                            var item = adventure.FindItem(item_id);

                            items.Add(item, ammount);
                        }
                    }
                }
            }

            public string id;
            public string name;
            public string description;
            public List<Connection> connections = new List<Connection>();

            public ItemContainer items = new ItemContainer();

            public List<Prop> props = new List<Prop>();

            public Area(string id)
            {
                this.id = id;
            }
            
            public Prop FindProp(string inputArg)
            {
                int index = 1;
                foreach (var prop in this.props)
                {
                    if (Compare(inputArg, index, prop.name))
                    {
                        return prop;
                    }

                    index++;
                }

                return null;
            }

            public void Load(Adventure adventure, DataNode node)
            {
                this.name = node.GetString("name", "???");
                this.description = node.GetString("desc");

                foreach (var child in node.Children)
                {
                    if (child.Name == "contains")
                    {
                        var ammount = child.GetInt32("ammount", 1);
                        var item_id = child.GetString("item");

                        var item = adventure.FindItem(item_id);

                        items.Add(item, ammount);
                    }

                    if (child.Name == "connects")
                    {
                        var connection = new Connection(adventure, child);

                        this.connections.Add(connection);
                    }

                    if (child.Name == "prop")
                    {
                        var prop = new Prop(adventure, child);
                        props.Add(prop);
                    }
                }
            }
        }

        public class State
        {
            public Area area;
            public ItemContainer items = new ItemContainer();
        }

        public class Entry
        {
            public readonly string id;

            private List<Command> _commands = new List<Command>();
            public int CommmandCount { get { return _commands.Count; } }

            public Entry(DataNode node)
            {
                this.id = node.GetString("id");

                foreach (var child in node.Children)
                {
                    if (child.Name.Equals("id"))
                    {
                        continue;
                    }

                    var cmd = Command.FromNode(child);
                    if (cmd != null)
                    {
                        _commands.Add(cmd);
                    }
                }
            }

            public Command GetCommand(int index)
            {
                return _commands[index];
            }
        }

        private Dictionary<string, Area> _areas = new Dictionary<string, Area>();
        public Area FindArea(string id)
        {
            if (_areas.ContainsKey(id))
            {
                return _areas[id];
            }

            return null;
        }

        private Dictionary<string, Item> _items = new Dictionary<string, Item>();
        public Item FindItem(string id)
        {
            if (_items.ContainsKey(id))
            {
                return _items[id];
            }

            return null;
        }

        private Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        public Entry FindEntry(string id)
        {
            if (_entries.ContainsKey(id))
            {
                return _entries[id];
            }

            return null;
        }

        public Adventure(Driver driver)
        {
            this.driver = driver;
        }

        public void Load(string fileName)
        {
            var xml = File.ReadAllText(fileName);
            var root = XMLReader.ReadFromString(xml);
            root = root["adventure"];

            foreach (var child in root.Children)
            {
                switch (child.Name.ToLower())
                {
                    case "entry":
                        {
                            var entry = new Entry(child);
                            _entries[entry.id] = entry;
                            break;
                        }

                    case "area":
                        {
                            var area_id = child.GetString("id");
                            var area = new Area(area_id);
                            _areas[area.id] = area;
                            break;
                        }

                    case "item":
                        {
                            var item_id = child.GetString("id");
                            var item = new Item(child);
                            _items[item.id] = item;
                            break;
                        }
                }
            }


            foreach (var child in root.Children)
            {
                switch (child.Name.ToLower())
                {
                    case "area":
                        {
                            var area_id = child.GetString("id");
                            var area = FindArea(area_id);
                            area.Load(this, child);
                            break;
                        }
                }
            }

            this.Reset();
        }

        private Entry _currentEntry;
        private int _currentCommandIndex;

        private string lastDirection;

        public void SetEntry(string id)
        {
            _currentEntry = FindEntry(id);
            _currentCommandIndex = 0;
        }

        private void PushCommand(Command cmd)
        {
            _commandQueue.Enqueue(cmd);
        }

        private static bool Compare(string input, int index, string name)
        {
            return input == index.ToString() || input.Equals(name, StringComparison.OrdinalIgnoreCase);
        }

        private void ProcessInput(Driver driver, Adventure.State state)
        {
            driver.Prompt();
            var input = driver.ReadLine();
            var temp = input.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var tag = temp[0].ToLowerInvariant();
            var inputArg = temp.Length > 1 ? temp[1] : "";
            switch (tag)
            {
                default:
                    {
                        driver.WriteLine("Command not understood...");
                        break;
                    }

                case "restart":
                    {
                        this.Reset();
                        break;
                    }

                case "quit":
                    {
                        PushCommand(new SelectCommand("Are you sure?", new List<SelectCommand.Option>{
                            new SelectCommand.Option() { text = "yes", command =  new CustomCommand( _ => {
                                driver.WriteLine("Quitting...");
                                this.isFinished = true;
                                return true;
                            })},
                            new SelectCommand.Option() { text = "no", command =  new ContinueCommand()} }
                            ));
                        break;
                    }
          
                case "items":
                    {
                        if (state.items.entries.Count>0)
                        {
                            driver.WriteLine("Inventory:");
                            foreach (var entry in state.items.entries)
                            {
                                var ammount = entry.Value;
                                if (ammount <=0 ) { continue; }
                                var item = entry.Key;
                                driver.WriteLine($"{item.name} (x {ammount})");
                            }
                        }
                        else
                        {
                            driver.WriteLine("Your inventory is empty...");
                        }
                        break;
                    }

                case "drop":
                    {
                        int index = 1;
                        Item dropped = null;
                        foreach (var entry in state.items.entries)
                        {
                            var ammount = entry.Value;
                            if (ammount <= 0) { continue; }

                            var item = entry.Key;

                            if (Compare(inputArg,  index, item.name))
                            {
                                dropped = item;
                                break;
                            }

                            index++;
                        }

                        if (dropped != null)
                        {
                            state.items.Move(dropped, state.area.items);
                            driver.WriteLine($"Dropped {dropped.name}.");
                        }
                        break;
                    }

                case "take":
                    {
                        FindTargetAndContainer(temp, out Target target, out ItemContainer container);

                        Item taken = target as Item;

                        if (taken != null)
                        {
                            container.Move(taken, state.items);
                            driver.WriteLine($"Took {taken.name}.");
                        }
                        else
                        {
                            driver.WriteLine($"Can not take that.");
                        }

                        break;
                    }

                case "open":
                    {
                        Target target = null;
                        int index = 1;
                        foreach (var prop in state.area.props)
                        {
                            if (Compare(inputArg, index, prop.name))
                            {
                                if (prop.)
                                break;
                            }
                            index++;
                        }

                        if (target == null)
                        {
                            driver.WriteLine($"Can not open that.");
                        }

                        break;
                    }

                case "go":
                    {
                        var direction = (inputArg == "back") ? GetOppositeDirection(lastDirection) : inputArg;

                        Area.Connection conn = null;

                        if (state.area != null)
                        {
                            foreach (var connection in state.area.connections)
                            {
                                if (connection.direction == direction)
                                {
                                    conn = connection;
                                    if (connection.open)
                                    {
                                        driver.WriteLine($"You moved [{direction}].");
                                        lastDirection = direction;
                                        state.area = connection.area;
                                    }
                                    else
                                    {
                                        driver.WriteLine($"Can not go [{direction}]. The {connection.type} is blocking the way.");
                                    }
                                    break;
                                }
                            }
                        }

                        if (conn == null)
                        {
                            driver.WriteLine("Not possible to go in that direction...");
                        }

                        if (inputArg == "back")
                        {
                            lastDirection = null;
                        }

                        break;
                    }

                case "examine":
                    {
                        FindTargetAndContainer(temp, out Target target, out ItemContainer container);

                        if (target != null)
                        {
                            driver.WriteLine(target.desc);

                            var prop = target as Area.Prop;
                            if (prop != null)
                            {
                                int index = 1;
                                foreach (var entry in prop.items.entries)
                                {
                                    var item = entry.Key;
                                    driver.WriteLine($"Inside there is: {item.name} [{index}].");
                                    index++;
                                }
                            }
                        }
                        else
                        {
                            driver.WriteLine("Can not examine that...");
                        }

                        break;
                    }

                case "look":
                    {
                        if (state.area != null)
                        {
                            driver.WriteLine(state.area.description);

                            int index = 1;
                            foreach (var prop in state.area.props)
                            {
                                driver.WriteLine($"There is an {prop.name} [{index}].");
                                index++;
                            }

                            foreach (var connection in state.area.connections)
                            {
                                driver.WriteLine($"There is a {connection.type} [{connection.direction}].");
                            }

                            index = 1;
                            foreach (var entry in state.area.items.entries)
                            {
                                int ammount = entry.Value;
                                if (ammount<=0)
                                {
                                    continue;
                                }

                                var item = entry.Key;

                                var article = ammount > 1 ? "some" : "one";

                                driver.WriteLine($"You see {article} {item.name} [{index}] laying around");

                                index++;
                            }
                        }
                        break;
                    }
            }
        }

        private void FindTargetAndContainer(string[] temp, out Target item, out ItemContainer container)
        {
            var inputArg = temp.Length>1? temp[1] : "";
            container = null;

            if (temp.Length>=4 && temp[2] == "from")
            {
                var secondArg = temp[3];

                if (secondArg == "bag")
                {
                    container = this.state.items;
                }
                else
                {
                    int index = 1;
                    foreach (var prop in state.area.props)
                    {
                        if (Compare(secondArg, index, prop.name))
                        {
                            container = prop.items;
                            break;
                        }

                        index++;
                    }
                }

                if (container != null)
                {
                    int index = 1;
                    foreach (var entry in container.entries)
                    {
                        if (Compare(inputArg, index, entry.Key.name))
                        {
                            item = entry.Key;
                            return;
                        }
                    }
                }
            }
            else
            {
                int index = 1;
                foreach (var entry in state.area.items.entries)
                {
                    if (Compare(inputArg, index, entry.Key.name))
                    {
                        container = state.area.items;
                        item = entry.Key;
                        return;
                    }
                }

                foreach (var prop in state.area.props)
                {
                    if (Compare(inputArg, index, prop.name))
                    {
                        container = state.area.items;
                        item = prop;
                        return;
                    }
                }
            }

            item = null;
        }

        private string GetOppositeDirection(string dir)
        {
            switch (dir)
            {
                case "north": return "south";
                case "south": return "north";

                case "west": return "east";
                case "east": return "west";

                case "down": return "up";
                case "up": return "down";

                default: return null;
            }
        }

        private Queue<Command> _commandQueue = new Queue<Command>();

        private bool ExecuteCommand(Command cmd)
        {
            var result = cmd.Execute(this);

            if (!result)
            {
                do
                {
                    driver.Prompt();
                    var input = driver.ReadLine();
                    var next = cmd.ReceiveInput(input);

                    if (next != null)
                    {
                        PushCommand(next);
                        return false;
                    }
                } while (true);
            }

            return true;
        }

        private bool isFinished = false;

        public void Reset()
        {
            this.state = new State();
            SetEntry("1");
            isFinished = false;
        }

        public bool Execute()
        {
            if (isFinished)
            {
                return false;
            }

            if (_commandQueue.Count>0)
            {
                var cmd = _commandQueue.Dequeue();
                ExecuteCommand(cmd);
            }
            else
            if (_currentCommandIndex < _currentEntry.CommmandCount)
            {
                var cmd = _currentEntry.GetCommand(_currentCommandIndex);                
                var result = ExecuteCommand(cmd);
                _currentCommandIndex++;
             }
            else
            {
                ProcessInput(driver, state);
            }

            return true;
        }

    }
}
