using FirebirdSql.Data.FirebirdClient;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Test_App_Sente;

namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
                return 1;
            }
            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                        {
                            try
                            {

                                string dbDir = GetArgValue(args, "--db-dir");
                                string scriptsDir = GetArgValue(args, "--scripts-dir");

                                if(BuildDatabase(dbDir, scriptsDir))
                                    Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                                return 0;
                            }
                            catch(Exception ex)
                            {
                                Console.WriteLine("Błąd podczas budowania bazy danych: " + ex.Message);
                                return -1;
                            }
                        }

                    case "export-scripts":
                        {
                            
                            string connStr = GetArgValue(args, "--connection-string");
                            string outputDir = GetArgValue(args, "--output-dir");

                            if(ExportScripts(connStr, outputDir))
                                Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                            return 0;
                        }

                    case "update-db":
                        {
                            try
                            {

                            string connStr = GetArgValue(args, "--connection-string");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            UpdateDatabase(connStr, scriptsDir);
                            Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                            return 0;
                            }
                            catch(Exception ex)
                            {
                                Console.WriteLine("Błąd podczas aktualizacji bazy danych: " + ex.Message);
                                return -1;
                            }
                        }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");
            return args[idx + 1];
        }

        /// <summary>
        /// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static bool BuildDatabase(string databaseDirectory, string scriptsDirectory)
        {
            // 1) Utwórz pustą bazę danych FB 5.0 w katalogu databaseDirectory.
            string databaseFilePath = Path.Combine(databaseDirectory, "DATABASE.FDB");
            string connectionString = $"Database={databaseFilePath}; DataSource=localhost; Port=3050; User=SYSDBA; Password=masterkey; Dialect=3; CharSet=UTF8; Role=; ServerType=0";
            try
            {

                CreateFirebirdDatabase(connectionString);
                Console.WriteLine($"Baza danych została pomyślnie utworzona: {databaseFilePath}");
                // 2) Wczytaj i wykonaj kolejno skrypty z katalogu scriptsDirectory
                // (tylko domeny, tabele, procedury).
                var check = ExecuteScripts(connectionString, scriptsDirectory);
                if (check)
                    Console.WriteLine($"Skrypty z katalogu '{scriptsDirectory}' zostały pomyślnie wykonane.");
                else
                    return false;
                return true;
                // 3) Obsłuż błędy i wyświetl raport.

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }

            
        }
        /// <summary>
        /// Tworzy pustą bazę danych Firebird.
        /// </summary>
        private static void CreateFirebirdDatabase(string connectionString)
        {
            var csb = new FbConnectionStringBuilder(connectionString);
            string databasePath = csb.Database;
            string? directory = Path.GetDirectoryName(databasePath);
            try
            {
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                if (File.Exists(databasePath))
                {
                    throw new Exception("Plik z taką nazwą już istnieje");
                }
                FbConnection.CreateDatabase(connectionString);
            }
            catch (FbException ex)
            {
                throw new Exception("Nie można utworzyć bazy danych Firebird: " + ex.Message, ex);
            }
        }
        /// <summary>
        /// Wczytuje i wykonuje skrypty SQL z podanego katalogu.
        /// Skrypty są wykonywane w kolejności alfabetycznej (np. 01_DOMAINS.sql, 02_TABLES.sql...).
        /// </summary>
        private static bool ExecuteScripts(string connectionString, string scriptsDirectory)
        {
            // Ustalanie kolejności skryptów: 01_DOMAINS, 02_TABLES, 03_PROCEDURES, itd.
            var scriptFiles = Directory.GetFiles(scriptsDirectory, "*.sql")
                .OrderBy(f => Path.GetFileName(f))
                .ToList(); // Konwersja do listy, aby sprawdzić Count

            // NOWA KONTROLA: Sprawdzenie, czy istnieją jakiekolwiek pliki do wykonania
            if (scriptFiles.Count == 0)
            {
                Console.WriteLine($"Ostrzeżenie: Nie znaleziono żadnych plików *.sql w katalogu: {scriptsDirectory}. Pomijanie wykonania skryptów.");
                return false;
            }

            using (var connection = new FbConnection(connectionString))
            {
                connection.Open();

                // Używamy domyślnego terminatora SQL (średnik) - zmienna ta jest nieużywana
                // ponieważ cała logika terminatora jest w SplitScriptByTerminator.
                // string currentTerminator = ";"; 

                foreach (var scriptFile in scriptFiles)
                {
                    Console.WriteLine($"-> Wykonywanie skryptu: {Path.GetFileName(scriptFile)}");
                    string sqlScript = File.ReadAllText(scriptFile, Encoding.UTF8);

                    // Używamy wyrażenia regularnego do podziału skryptu, uwzględniając niestandardowe terminatory (SET TERM)
                    var scriptCommands = SplitScriptByTerminator(sqlScript);

                    foreach (var commandText in scriptCommands)
                    {
                        if (string.IsNullOrWhiteSpace(commandText)) continue;

                        // Sprawdź, czy to jest komenda SET TERM
                        if (commandText.StartsWith("SET TERM", StringComparison.OrdinalIgnoreCase))
                        {
                            // Komenda SET TERM jest używana przez parser, ale nie jest wykonywana bezpośrednio 
                            // przez ExecuteNonQuery w ADO.NET.
                            continue;
                        }

                        // Wykonanie komendy
                        ExecuteSingleCommand(connection, commandText);
                    }
                }
                return true;
            }
        }
        /// <summary>
        /// Pomocnicza funkcja wykonująca pojedyncze polecenie SQL.
        /// </summary>
        private static void ExecuteSingleCommand(FbConnection connection, string commandText)
        {
            // W Firebird DDL (CREATE TABLE, CREATE DOMAIN) musi być zatwierdzony (COMMIT)
            // każda komenda musi być wykonana w osobnej transakcji, która musi zostać zatwierdzona.
            using (var transaction = connection.BeginTransaction())
            using (var command = new FbCommand(commandText, connection, transaction))
            {
                try
                {
                    command.ExecuteNonQuery();
                    transaction.Commit();
                }
                catch (FbException ex)
                {
                    transaction.Rollback();
                    // Lepsze logowanie błędu, używając File.ReadAllText(scriptFile) do kontekstu
                    throw new Exception($"Błąd SQL: {ex.Message}\nLinia kodu: {commandText.Trim()}", ex);
                }
            }
        }
        /// <summary>
        /// Dzieli skrypt na pojedyncze komendy, prawidłowo obsługując bloki SET TERM.
        /// </summary>
        private static IEnumerable<string> SplitScriptByTerminator(string sqlScript)
        {
            // Wyrażenie regularne do znajdowania bloków SET TERM, co pozwala określić bieżący separator.
            // Domyślny terminator to średnik.

            // Uproszczone założenie: Wszędzie, gdzie nie ma bloku procedury, separatorem jest ';'.
            // Skrypty procedur (03_PROCEDURES.sql) używają SET TERM $$ ; (jak w naszym generatorze).

            // Jeśli skrypt zawiera "SET TERM", dzielimy go niestandardowo.
            if (sqlScript.Contains("SET TERM", StringComparison.OrdinalIgnoreCase))
            {
                // 1. Dzielimy skrypt po SET TERM
                var parts = Regex.Split(sqlScript, @"(SET\s+TERM\s+[^;]+;)", RegexOptions.IgnoreCase | RegexOptions.Multiline)
                                 .Where(p => !string.IsNullOrWhiteSpace(p));

                string currentTerm = ";";
                var commands = new List<string>();

                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();

                    if (trimmedPart.StartsWith("SET TERM", StringComparison.OrdinalIgnoreCase))
                    {
                        // Znaleziono nowy terminator (np. SET TERM $$ ;)
                        // Wyodrębniamy nowy terminator (np. '$$' lub ';')
                        var match = Regex.Match(trimmedPart, @"SET\s+TERM\s+(.+);", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            currentTerm = match.Groups[1].Value.Trim();
                        }
                        // Dodajemy samą komendę SET TERM do wykonania (chociaż FbCommand ją zignoruje)
                        // commands.Add(trimmedPart);
                        continue; // Nie dodajemy samego SET TERM jako komendy do wykonania w pętli
                    }
                    else
                    {
                        // Dzielimy ten blok na komendy za pomocą aktualnego terminatora
                        if (currentTerm == ";")
                        {
                            // Normalny blok (np. CREATE TABLE)
                            commands.AddRange(trimmedPart.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                         .Select(c => c.Trim()));
                        }
                        else
                        {
                            // Blok procedury/wyzwalacza (terminator to np. $$)
                            commands.AddRange(trimmedPart.Split(new[] { currentTerm }, StringSplitOptions.RemoveEmptyEntries)
                                                         .Select(c => c.Trim()));
                        }
                    }
                }
                return commands.Where(c => !string.IsNullOrEmpty(c.Trim()));
            }
            else
            {
                // Brak SET TERM, prosty podział po średniku
                return sqlScript.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(c => c.Trim())
                                .Where(c => !string.IsNullOrEmpty(c));
            }
        }


        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static bool ExportScripts(string connectionString, string outputDirectory)
        {

            // Upewniamy się, że katalog wyjściowy istnieje
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            try
            {
                // 1) Połącz się z bazą danych przy użyciu connectionString.
                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();
                    Console.WriteLine("Połączono z bazą danych.");

                    // 2) Pobierz metadane i 3) Wygeneruj pliki .sql

                    // Etap 1: Domeny
                    var domainScripts = GenerateDomainScripts(connection);
                    SaveScripts(domainScripts, outputDirectory, "01_DOMAINS.sql");
                    Console.WriteLine("Wyeksportowano skrypty Domen (Domains).");

                    // Etap 2: Tabele (z kolumnami)
                    var tableScripts = GenerateTableScripts(connection);
                    SaveScripts(tableScripts, outputDirectory, "02_TABLES.sql");
                    Console.WriteLine("Wyeksportowano skrypty Tabel (Tables).");

                    // Etap 3: Procedury
                    var procedureScripts = GenerateProcedureScripts(connection);
                    SaveScripts(procedureScripts, outputDirectory, "03_PROCEDURES.sql");
                    Console.WriteLine("Wyeksportowano skrypty Procedur (Procedures).");

                    Console.WriteLine($"\n⭐ Zakończono eksport. Skrypty zapisano w: {outputDirectory}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas eksportu skryptów: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// Zapisuje listę skryptów SQL do pojedynczego pliku w określonym katalogu.
        /// </summary>
        /// <param name="scripts">Lista łańcuchów znaków reprezentujących skrypty SQL (CREATE DOMAIN, CREATE TABLE, itp.).</param>
        /// <param name="directory">Katalog docelowy, w którym ma zostać utworzony plik.</param>
        /// <param name="fileName">Nazwa pliku (np. "01_DOMAINS.sql").</param>
        private static void SaveScripts(List<string> scripts, string directory, string fileName)
        {
            // Konstruujemy pełną ścieżkę pliku
            string fullPath = Path.Combine(directory, fileName);

            // Używamy StringBuilder do efektywnego łączenia wszystkich skryptów
            var contentBuilder = new StringBuilder();


            foreach (var script in scripts)
            {
                // Dodajemy każdy skrypt, zakończony średnikiem i nową linią,
                // z wyjątkiem przypadków, gdy skrypt już zawiera własne terminatory (jak w procedurach)

                contentBuilder.AppendLine(script);

                // Dodajemy średnik, jeśli nie jest to skrypt procedury (który używa SET TERM)
                // Dla uproszczenia, zakładamy, że tylko skrypty procedur używają SET TERM i nie wymagają końcowego średnika.
                if (!script.Contains("SET TERM"))
                {
                    contentBuilder.AppendLine(";");
                }
                contentBuilder.AppendLine(); // Pusta linia dla lepszej czytelności
            }

            // Zapisujemy cały zebrany tekst do pliku
            File.WriteAllText(fullPath, contentBuilder.ToString(), Encoding.UTF8);
        }
        /// <summary>
        /// Generuje skrypty CREATE DOMAIN.
        /// </summary>
        private static List<string> GenerateDomainScripts(FbConnection connection)
        {
            var scripts = new List<string>();

            string sql = @"
SELECT
    f.RDB$FIELD_NAME,
    f.RDB$FIELD_TYPE,
    f.RDB$FIELD_SUB_TYPE,
    f.RDB$FIELD_LENGTH,
    f.RDB$FIELD_SCALE,
    f.RDB$CHARACTER_LENGTH,
    f.RDB$DEFAULT_SOURCE,
    f.RDB$NULL_FLAG,
    f.RDB$VALIDATION_SOURCE
FROM RDB$FIELDS f
WHERE f.RDB$SYSTEM_FLAG = 0 AND f.RDB$FIELD_NAME NOT LIKE 'RDB$%'
ORDER BY f.RDB$FIELD_NAME;
";

            using var cmd = new FbCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                string domainName = reader.GetString(0).Trim();

                int fieldType = reader.GetInt32(1);
                int fieldSubType = reader.IsDBNull(2) ? (short)0 : reader.GetInt16(2);
                int fieldLength = reader.GetInt32(3);
                int fieldScale = reader.GetInt32(4);
                int charLength = reader.IsDBNull(5) ? (short)0 : reader.GetInt32(5);

                string? defaultSrc = reader.IsDBNull(6) ? null : reader.GetString(6).Trim();
                bool notNull = !reader.IsDBNull(7) && reader.GetInt16(7) == 1;
                string? checkSrc = reader.IsDBNull(8) ? null : reader.GetString(8).Trim();

                string typeDecl = GetTypeDeclaration(
                    fieldType, fieldSubType, fieldLength, fieldScale, charLength);

                var sb = new StringBuilder();
                sb.Append($"CREATE DOMAIN {domainName} AS {typeDecl}");

                if (!string.IsNullOrEmpty(defaultSrc))
                    sb.Append(" " + defaultSrc);

                if (notNull)
                    sb.Append(" NOT NULL");

                if (!string.IsNullOrEmpty(checkSrc))
                    sb.Append(" " + checkSrc);

                sb.Append(";");

                scripts.Add(sb.ToString());
            }

            return scripts;
        }


        /// <summary>
        /// Tworzy pełną deklarację typu danych SQL (np. VARCHAR(100), NUMERIC(15, 2)),
        /// uwzględniając nazwę typu, jego ID, długość i skalę (miejsca po przecinku).
        /// </summary>
        
        private static string GetTypeDeclaration(
    int type,
    int subType,
    int length,
    int scale,
    int charLength)
        {
            return type switch
            {
                // Integer types
                7 => scale < 0 ? $"NUMERIC(4,{Math.Abs(scale)})" : "SMALLINT",
                8 => scale < 0 ? $"NUMERIC(9,{Math.Abs(scale)})" : "INTEGER",
                16 => scale < 0 ? $"NUMERIC(18,{Math.Abs(scale)})" : "BIGINT",

                // Floating point
                10 => "FLOAT",
                27 => "DOUBLE PRECISION",

                // Date / time
                12 => "DATE",
                13 => "TIME",
                35 => "TIMESTAMP",

                // Character
                14 => $"CHAR({charLength})",
                37 => $"VARCHAR({charLength})",

                // Blob
                261 => subType switch
                {
                    1 => "BLOB SUB_TYPE TEXT",
                    _ => "BLOB"
                },

                _ => $"UNKNOWN_TYPE_{type}"
            };
        }


        private static List<string> GenerateTableScripts(FbConnection connection)
        {
            string sql = @"
SELECT
    r.RDB$RELATION_NAME AS TABLE_NAME,
    s.RDB$FIELD_NAME AS COLUMN_NAME,
    s.RDB$FIELD_POSITION AS COLUMN_POSITION,
    s.RDB$NULL_FLAG AS NOT_NULL_FLAG,
    s.RDB$DEFAULT_SOURCE AS DEFAULT_VALUE,
    f.RDB$FIELD_LENGTH AS FIELD_LENGTH,
    f.RDB$FIELD_SCALE AS FIELD_SCALE,
    f.RDB$FIELD_TYPE AS FIELD_TYPE_ID,
    f.RDB$FIELD_SUB_TYPE AS FIELD_SUB_TYPE,
    f.RDB$CHARACTER_LENGTH AS ""CHAR_LENGTH"",
    f.RDB$FIELD_NAME AS DOMAIN_NAME
FROM
    RDB$RELATIONS r
JOIN
    RDB$RELATION_FIELDS s
    ON r.RDB$RELATION_NAME = s.RDB$RELATION_NAME
JOIN
    RDB$FIELDS f
    ON s.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
WHERE
    r.RDB$SYSTEM_FLAG = 0
ORDER BY
    TABLE_NAME, COLUMN_POSITION;
";

            var tableColumns = new Dictionary<string, List<ColumnMetadata>>();

            using (var command = new FbCommand(sql, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string tableName = reader.GetString(0).Trim();

                    var column = new ColumnMetadata
                    {
                        TableName = tableName,
                        ColumnName = reader.GetString(1).Trim(),
                        Position = reader.GetInt16(2),
                        IsNotNull = !reader.IsDBNull(3) && reader.GetInt16(3) == 1,
                        DefaultValueSource = reader.IsDBNull(4) ? null : reader.GetString(4)?.Trim(),
                        FieldLength = reader.IsDBNull(5) ? 0 : reader.GetInt16(5),
                        FieldScale = reader.IsDBNull(6) ? 0 : reader.GetInt16(6),
                        FieldTypeId = reader.IsDBNull(7) ? (short)0 : reader.GetInt16(7),
                        FieldSubType = reader.IsDBNull(8) ? (short)0 : reader.GetInt16(8),
                        CharLength = reader.IsDBNull(9) ? 0 : reader.GetInt16(9),
                        DomainName = reader.IsDBNull(10) ? null : reader.GetString(10).Trim()
                    };

                    if (!tableColumns.ContainsKey(tableName))
                        tableColumns[tableName] = new List<ColumnMetadata>();

                    tableColumns[tableName].Add(column);
                }
            }

            var scripts = new List<string>();

            foreach (var entry in tableColumns)
            {
                string tableName = entry.Key;
                List<ColumnMetadata> columns = entry.Value;

                var sb = new StringBuilder($"CREATE TABLE {tableName} (\n");

                for (int i = 0; i < columns.Count; i++)
                {
                    ColumnMetadata col = columns[i];
                    sb.Append($"    {col.ColumnName} ");

                    if (!string.IsNullOrEmpty(col.DomainName) && !col.DomainName.StartsWith("RDB$"))
                    {
                        sb.Append(col.DomainName);
                    }
                    else
                    {
                        sb.Append(GetTypeDeclaration( col.FieldTypeId, col.FieldSubType, col.FieldLength, col.FieldScale, col.CharLength));
                    }

                    if (col.IsNotNull)
                        sb.Append(" NOT NULL");

                    if (!string.IsNullOrEmpty(col.DefaultValueSource))
                        sb.Append(" " + col.DefaultValueSource);

                    sb.Append(i < columns.Count - 1 ? ",\n" : "\n");
                }

                sb.Append(");");
                scripts.Add(sb.ToString());
            }

            return scripts;
        }

        private static List<string> GenerateProcedureScripts(FbConnection connection)
        {
            var scripts = new List<string>();

            // 1. Główne zapytanie: Pobranie metadanych procedur
            string procedureSql = @"
SELECT
    p.RDB$PROCEDURE_NAME AS PROCEDURE_NAME,
    p.RDB$PROCEDURE_SOURCE AS PROCEDURE_SOURCE
FROM
    RDB$PROCEDURES p
WHERE
    p.RDB$SYSTEM_FLAG = 0
ORDER BY
    p.RDB$PROCEDURE_NAME;";

            // 2. Zapytanie pomocnicze: Pobranie parametrów dla konkretnej procedury
            string paramSql = @"
SELECT
    p.RDB$PARAMETER_NAME AS PARAMETER_NAME,
    p.RDB$PARAMETER_TYPE AS PARAMETER_TYPE, -- 0: INPUT, 1: OUTPUT
    f.RDB$FIELD_NAME AS DOMAIN_NAME,
    f.RDB$FIELD_TYPE AS FIELD_TYPE_ID,
    f.RDB$FIELD_SUB_TYPE AS FIELD_SUB_TYPE,
    f.RDB$FIELD_LENGTH AS FIELD_LENGTH,
    f.RDB$CHARACTER_LENGTH AS RDB_CHAR_LENGTH,
    f.RDB$FIELD_SCALE AS FIELD_SCALE
FROM
    RDB$PROCEDURE_PARAMETERS p
JOIN
    RDB$FIELDS f
    ON p.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
WHERE
    p.RDB$PROCEDURE_NAME = @ProcedureName
ORDER BY
    p.RDB$PARAMETER_TYPE, p.RDB$PARAMETER_NUMBER;";

            using (var cmd = new FbCommand(procedureSql, connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name = reader.GetString(0).Trim();
                    string? source = reader.IsDBNull(1) ? null : reader.GetString(1);

                    if (source == null) continue;

                    // 3. Ustawienie niestandardowego terminatora
                    var sb = new StringBuilder();
                    sb.AppendLine("SET TERM $$ ;");
                    sb.Append($"CREATE PROCEDURE {name} ");

                    // 4. Pobranie i przygotowanie parametrów
                    var inputParams = new List<string>();
                    var outputParams = new List<string>();

                    using (var paramCmd = new FbCommand(paramSql, connection))
                    {
                        paramCmd.Parameters.AddWithValue("@ProcedureName", name);
                        using (var paramReader = paramCmd.ExecuteReader())
                        {
                            while (paramReader.Read())
                            {
                                string paramName = paramReader.GetString(0).Trim();
                                int paramType = paramReader.GetInt16(1);
                                string domainName = paramReader.GetString(2).Trim();

                                // 5. Pobranie dodatkowych danych dla dynamicznego typu
                                short fieldTypeId = paramReader.IsDBNull(3) ? (short)0 : paramReader.GetInt16(3);
                                short fieldSubType = paramReader.IsDBNull(4) ? (short)0 : paramReader.GetInt16(4);
                                short fieldLength = paramReader.IsDBNull(5) ? (short)0 : paramReader.GetInt16(5);
                                short charLength = paramReader.IsDBNull(6) ? (short)0 : paramReader.GetInt16(6);
                                short fieldScale = paramReader.IsDBNull(7) ? (short)0 : paramReader.GetInt16(7);

                                // 6. Jeśli domena systemowa (RDB$...) → używamy dynamicznego typu
                                string typeDeclaration = domainName.StartsWith("RDB$")
                                    ? GetTypeDeclaration(fieldTypeId, fieldSubType, fieldLength, fieldScale, charLength)
                                    : domainName; // domena użytkownika

                                string paramDeclaration = $"{paramName} {typeDeclaration}";

                                if (paramType == 0) inputParams.Add(paramDeclaration);
                                else outputParams.Add(paramDeclaration);
                            }
                        }
                    }

                    // 7. Dodanie parametrów wejściowych
                    if (inputParams.Any())
                        sb.Append($"({string.Join(", ", inputParams)})");

                    // 8. Dodanie parametrów wyjściowych
                    if (outputParams.Any())
                    {
                        sb.AppendLine();
                        sb.Append($"RETURNS ({string.Join(", ", outputParams)})");
                    }

                    // 9. Dodanie ciała procedury
                    sb.AppendLine(" AS");
                    sb.AppendLine(source.Trim());

                    // 10. Zakończenie definicji procedury
                    sb.AppendLine("$$");
                    sb.AppendLine("SET TERM ; $$");

                    scripts.Add(sb.ToString());
                }
            }

            return scripts;
        }

        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            // 1) Połącz się z bazą danych przy użyciu connectionString.
            // 2) Wykonaj skrypty z katalogu scriptsDirectory (tylko obsługiwane elementy).
            // 3) Zadbaj o poprawną kolejność i bezpieczeństwo zmian.

            // Walidacja katalogu
            if (!Directory.Exists(scriptsDirectory))
            {
                Console.WriteLine($"Błąd: Katalog ze skryptami nie istnieje: {scriptsDirectory}");
                return;
            }

            try
            {
                Console.WriteLine("Łączenie z bazą danych w celu aktualizacji...");

                // Próba połączenia, aby sprawdzić, czy baza danych jest dostępna
                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();
                    Console.WriteLine("Połączono pomyślnie. Rozpoczynanie wykonywania skryptów...");
                }

                // Wykonanie skryptów (zostanie ponownie otwarte połączenie wewnątrz ExecuteScripts)
                if (ExecuteScripts(connectionString, scriptsDirectory))
                    Console.WriteLine("\nAktualizacja bazy danych zakończona pomyślnie.");

            }
            catch (FbException ex)
            {
               throw new Exception(ex.Message);
                // W przypadku błędu połączenia lub SQL, warto zwrócić błąd do Main+
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
    }
}
