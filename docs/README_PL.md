# NeonDeck Desktop Gadgets — v1.5.0 (Windows 11 Portable)

**Uruchamiasz tylko `NeonDeck.exe`** (dwuklik). Bez instalatora, bez okna CMD i bez wymagania Go/Pythona na komputerze użytkownika.

## Pierwsze uruchomienie

1. Rozpakuj **całe archiwum ZIP** np. do `C:\Portable\NeonDeck` — nie uruchamiaj pliku bezpośrednio z ZIP.
2. Zamknij poprzedni NeonDeck przez ikonę w zasobniku (`Exit NeonDeck`).
3. Kliknij dwukrotnie **NeonDeck.exe**. Przy pierwszym uruchomieniu skompiluje on w tle moduł `NeonDeckApp.exe` za pomocą składnika kompilatora .NET Framework znajdującego się w Windows. Na kolejnych uruchomieniach korzysta z gotowego modułu.
4. Ikona z zielono-żółtym wykresem wyświetla się przy zegarze Windows, często pod strzałką `^`. Dwuklik pokazuje/ukrywa panele, prawy przycisk otwiera menu i `Exit NeonDeck`.

**Ważne:** Do pierwszego uruchomienia lub aktualizacji kodu potrzebne są pliki `NeonDeck.cs`, `NeonDeck.ico` i kompilator C# .NET Framework 4.x pod `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (lub wariant 32-bit). Kompilator jest standardowo dostępny na wielu instalacjach Windows 11; jeżeli go brak, aplikacja pokaże błąd. To **launcher EXE**, a nie skompilowany tutaj gotowy program C# — gotowy `NeonDeckApp.exe` można też przygotować ze źródeł przez `scripts/build_windows.ps1` na Windows lub GitHub Actions.

## Jak działa

- **CPU:** obciążenie, zegar, pamięć, 16/więcej procesorów logicznych, 5 najaktywniejszych procesów.
- **GPU:** NVIDIA przez `nvidia-smi`, VRAM, temperatura, zegary, obciążenie, zasilanie, wentylatory, enkoder i PCIe (jeśli udostępnione).
- **NETWORK:** download/upload, ping, IP, wykres transferu i parametry połączenia.
- **DRIVES:** lokalne i zmapowane woluminy, litera, nazwa, zajętość/pojemność/wolne, stan, odczyt/zapis, opcjonalne SMART i temperatury; pionowy panel w jednej kolumnie.
- **Czyszczenie:** `EMPTY BIN` nieodwracalnie opróżnia Kosz, `CLEAN TEMP` usuwa starsze pliki tymczasowe z ograniczonego zakresu. Nie przywrócisz zawartości usuniętej z Kosza.
- Wszystkie panele możesz przesuwać, przypinać, blokować i pokazywać/ukrywać z menu ikony.
- Zapis pozycji i ustawień odbywa się w `settings.ini`, w katalogu programu.
- **Autostart:** `Start with Windows` wskazuje nowy `NeonDeck.exe`.

## Aktualizacja z v1.4.2

1. Zamknij program przez `Exit NeonDeck`.
2. Rozpakuj v1.5.0 do starego folderu, nadpisz stare pliki, **zachowaj `settings.ini`**.
3. Nowy `NeonDeck.exe` zastąpi stary. **Usuń poprzedni plik `START.cmd` i nie uruchamiaj go ponownie**, ponieważ stary skrypt mógłby nadpisać nowy launcher. Plik `NeonDeck.build` może zostać — nowa wersja rozpozna potrzebę przebudowania modułu.
4. Uruchom `NeonDeck.exe`.

## Czujniki

- Temperatura CPU/SSD: opcjonalnie LibreHardwareMonitor z WMI `root\LibreHardwareMonitor` — brak odczytu = `N/A`.
- Zmapowane dyski nie udostępniają lokalnego SMART ani temperatury serwera.

## Diagnostyka

Jeśli kompilacja nie powiedzie się, w folderze pojawi się `NeonDeck_build_error.txt`. Do zgłoszenia błędu dołącz **treść błędu**, nie dane osobiste z komputera. W paczce źródłowej GitHub są dodatkowe skrypty diagnostyczne.

## Usunięcie

1. Zamknij aplikację przez `Exit NeonDeck`.
2. Wyłącz `Start with Windows`, jeśli było włączone.
3. Usuń folder aplikacji. Nie ma deinstalatora ani usługi w tle.

**Status v1.5.0:** natywny `NeonDeck.exe` został zbudowany i statycznie zweryfikowany jako Windows GUI EXE z ikoną. Nie przetestowano uruchomienia ani kompilacji WinForms na prawdziwym Windows 11 w tym środowisku.
