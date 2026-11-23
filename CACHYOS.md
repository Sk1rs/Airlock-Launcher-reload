

## Сборка если ты еблан

```bash
sudo pacman -Syu --needed dotnet-sdk-10.0 python libx11 libice libsm fontconfig icu openssl zlib
python publish.py linux --x64-only
```

Подмодуль Robust.LoaderApi уже включён. Git для распакованных исходников не нужен.
Для восстановления пакетов и скачивания .NET нужен интернет блять ебать ты тупой нахуй.
Результат: SS14.Launcher_Linux.zip.

## Запуск готовой сборки если ты хуесос

Распакуй готовый архив в отдельную папку. Зависимости:

```bash
sudo pacman -Syu --needed libx11 libice libsm fontconfig icu openssl zlib
```

В сеансе Wayland нужен XWayland:

```bash
sudo pacman -S --needed xorg-xwayland
```

В папке лаунчера:

```bash
chmod +x SS14.Launcher bin_x64/SS14.Launcher bin_x64/loader/SS14.Loader dotnet_x64/dotnet
./SS14.Launcher
```

.NET включён в готовую сборку. Zapret в Linux-версию не входит ибо пошел нахуй.
Запуск интерфейса на CachyOS не проверял я чо еблан это говно ставить.

Пакеты .NET: https://wiki.archlinux.org/title/.NET
