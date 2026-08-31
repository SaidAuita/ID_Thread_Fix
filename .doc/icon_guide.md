# Инструкция по замене иконки в ID_Thread_Fix.exe

Для гарантированной правильной установки оригинальной иконки в `.exe` используется утилита **Resource Hacker**.

## Команда для PowerShell

```powershell
& "C:\Program Files (x86)\Resource Hacker\ResourceHacker.exe" `
  -open "C:\_CODE\indesign\ID_Thread_Fix\dist\ID_Thread_Fix.exe" `
  -save "C:\_CODE\indesign\ID_Thread_Fix\dist\ID_Thread_Fix.exe" `
  -action addoverwrite `
  -res "C:\_CODE\indesign\ID_Thread_Fix\assets\app.ico" `
  -mask "ICONGROUP,MAINICON," `
  -log CON
```

## Готовый скрипт

В репозитории есть готовый автоматический скрипт:
[`scripts/replace_icon.ps1`](../scripts/replace_icon.ps1)

Запуск:
```powershell
.\scripts\replace_icon.ps1
```

## Автоматическая интеграция в сборку

Скрипты сборки [`build.bat`](../build.bat) и [`build.ps1`](../build.ps1) автоматически проверяют наличие `ResourceHacker.exe` и вызывают замену `MAINICON` на этапе финализации бинарника.
