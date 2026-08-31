# Инструкция по замене иконки в ID_Thread_Fix.exe

Для гарантированной правильной установки оригинальной иконки Adobe InDesign в `.exe` используется утилита **Resource Hacker** и исходный файл иконки `ICON1_1.ico`.

## 1. Резервная копия

```powershell
Copy-Item `
  "C:\_CODE\indesign\ID_Thread_Fix\dist\ID_Thread_Fix.exe" `
  "C:\_CODE\indesign\ID_Thread_Fix\dist\ID_Thread_Fix_backup.exe" `
  -Force
```

## 2. Замена иконки через Resource Hacker

```powershell
& "C:\Program Files (x86)\Resource Hacker\ResourceHacker.exe" `
  -open "C:\_CODE\indesign\ID_Thread_Fix\dist\ID_Thread_Fix.exe" `
  -save "C:\_CODE\indesign\ID_Thread_Fix\dist\ID_Thread_Fix.exe" `
  -action addoverwrite `
  -res "C:\_CODE\indesign\ID_cpu\InDesign_icons\ICON1_1.ico" `
  -mask "ICONGROUP,MAINICON," `
  -log CON
```

## 3. Готовый скрипт

В репозитории подготовлен автоматический скрипт:
[`scripts/replace_icon.ps1`](../scripts/replace_icon.ps1)

Запуск:
```powershell
.\scripts\replace_icon.ps1
```
