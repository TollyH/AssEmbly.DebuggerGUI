using System.IO;
using Newtonsoft.Json;

namespace AssEmbly.DebuggerGUI
{
    public class ProgramStateSave(string programName)
    {
        public static readonly string SaveDirectory = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tolly Hill", "AssEmbly", "DebuggerGUI");

        public readonly string ProgramName = programName;
        public string ProgramSavePath => GetSavePath(ProgramName);

        public readonly HashSet<IBreakpoint> Breakpoints = new();
        public readonly Dictionary<string, ulong> Labels = new();
        public readonly Dictionary<ulong, string> SavedAddresses = new();
        public readonly Dictionary<Register, ulong> PersistentRegisterEdits = new();
        public readonly Dictionary<ulong, byte> PersistentMemoryEdits = new();

        private static readonly JsonSerializerSettings serializerSettings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto
        };

        public void Save()
        {
            Directory.CreateDirectory(SaveDirectory);

            File.WriteAllText(ProgramSavePath, JsonConvert.SerializeObject(this, serializerSettings));
        }

        public void Delete()
        {
            if (File.Exists(ProgramSavePath))
            {
                File.Delete(ProgramSavePath);
            }
        }

        public static ProgramStateSave LoadOrCreateNew(string programName, out bool loadedExisting)
        {
            string path = GetSavePath(programName);
            loadedExisting = File.Exists(path);
            return loadedExisting ? LoadFile(path) : new ProgramStateSave(programName);
        }

        public static ProgramStateSave LoadFile(string path)
        {
            return JsonConvert.DeserializeObject<ProgramStateSave>(File.ReadAllText(path), serializerSettings)
                ?? throw new JsonException("There was an error loading the specified file");
        }

        public static string GetSavePath(string programName)
        {
            return Path.ChangeExtension(Path.Join(SaveDirectory, programName), "dbg.json");
        }
    }
}
