# Instrukcja użycia aplikacji Test_App_Sente

## 1. Klonowanie repozytorium
Sklonuj repozytorium z GitHub:

```bash
git clone https://github.com/vichukin/Test_App_Sente
````

---

## 2. Kompilacja projektu

Zbuduj projekt w środowisku .NET (np. Visual Studio lub `dotnet build`):

```bash
cd Test_App_Sente
dotnet build
```

---

## 3. Przejście do katalogu z plikiem wykonywalnym

Otwórz konsolę i przejdź do katalogu, w którym znajduje się skompilowana aplikacja:

```bash
cd Test_App_Sente\Test_App_Sente\bin\Debug\net8.0
```

---

## 4. Uruchomienie programu

Uruchom aplikację za pomocą nazwy pliku wykonywalnego:

```bash
Test_App_Sente
```

---

## 5. Użycie aplikacji

Aplikacja obsługuje następujące polecenia:

### build-db

Budowanie bazy danych z plików skryptów SQL:

```bash
build-db --db-dir <ścieżka_do_bazy> --scripts-dir <ścieżka_do_skryptów>
```

### export-scripts

Eksport skryptów z istniejącej bazy danych:

```bash
export-scripts --connection-string <connStr> --output-dir <ścieżka_do_zapisu>
```

### update-db

Aktualizacja bazy danych przy użyciu skryptów:

```bash
update-db --connection-string <connStr> --scripts-dir <ścieżka_do_skryptów>
```

