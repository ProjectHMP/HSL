# HSL

Happiness Server Launcher. A Launcher & Manager for [HappinessMP](https://happinessmp.net/)

<img src="hsl.png"/>

## Features
- Add, Create & Update Server(s).
- Start, Stop, Restart Server(s).
- Run Multiple Servers In One Launcher (No extra windows).
- Quick Edit Server Settings.
- Start, Stop & Reload Resource(s).
- Automatic Server Start & Restart. (Scheduled & Crash Restarts)
- Automatic Delete Logs (_Including Backups_)
- Delete Cache
- Console Adaptation

It's a very basic program, what much too expect? 

## Download 
#### From [Releases](https://github.com/ProjectHMP/HSL/releases) 
###### _All releases are stable (Tested)_

## Prerequisits

**HSL** .NETCore version was lowered to `3.1` in preventing manually installing the latest & greatest framework.

Everyone _should_ have this, though if program doesn't launch, you probably don't. **[Download .Net Core 3.1](https://dotnet.microsoft.com/en-us/download/dotnet/3.1)**.

## Language Support

- English (Native/Default)
- German (Translated)

### Language Context

Inside of [Lang](HSL/Lang), will contain language (`.xaml`) files and a `languages.xaml` file.

`languages.xaml` is used to define a language to be loaded.
`x:Key` is the filename without extension, and the inner text the display name.

Take & Copy `en.xaml` to a new language file. Translate, and define your language in `language.xaml`

### Language Testing

You can place a language file by `HSL.exe` named `lang.xaml`.

It will not be a registered language, but it will _now_ be the default language.

## Build
You can clone this project and open it with **Visual Studio**, supporting .net core 3.1_ 

Otherwise, you _could_ build using with `dotnet` without **Visual Studio**. 
**Visual Studio Code** with extensions may also work, though this would be needed.

Install **[.Net Core 3.1](https://dotnet.microsoft.com/en-us/download/dotnet/3.1)**

### Build Command Executions

```batch
# dev? addon: -b dev
git clone https://github.com/ProjectHMP/HSL

# change directory (cd)
cd HSL/HSL

# build (creates a debug exe)
dotnet build

# build run (builds, then run)
dotnet run

# publish (release build)
dotnet publish
```
