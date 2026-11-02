# WinPreviewPlus

Бесплатное превью файлов в **Проводнике Windows** по **Alt+P** — без тяжёлых оболочек и без облака.

## Возможности

| Тип | Примеры |
|-----|---------|
| Архивы | zip, 7z, rar, tar, gz… |
| Office | docx, xlsx, **xls**, pptx |
| PDF | поиск по тексту, масштаб |
| Текст / код | txt, md, json, xml, ps1, cs… |
| Фото | jpg, png, webp, heic, **svg**… |
| Аудио / видео | mp3, flac, mp4, mkv… (видео/SVG — через WebView2) |

Поиск (**Ctrl+F**) в PDF, тексте и таблицах Office — одна панель «Найти».

## Установка

1. Скачайте **[WinPreviewPlus-Setup.exe](../../releases/latest)** из [Releases](../../releases/latest)  
2. Запустите → при необходимости отметьте зависимости (.NET 4.8, WebView2) → **Установить**  
3. В Проводнике выделите файл → **Alt+P**

Продукт ставится в `%LOCALAPPDATA%\WinPreviewPlus` (права администратора для самого превью не нужны).  
Если нет .NET Framework 4.8 или WebView2 Runtime — установщик может **скачать и поставить** их (будет запрос UAC), затем продолжит.

### Удаление

- кнопка **Удалить** в Setup  
- **Параметры → Приложения → WinPreviewPlus**  
- `WinPreviewPlus-Setup.exe /uninstall /silent`

## Требования

- Windows 10 / 11 (x64)  
- .NET Framework 4.8  
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — для видео и SVG (часто уже есть с Microsoft Edge)

## Сборка из исходников

Нужны Visual Studio 2022 Build Tools (MSBuild) и .NET Framework 4.8 targeting pack.

```powershell
.\packaging\Build-WinPreviewPlus.ps1
```

Результат: `dist\WinPreviewPlus-Setup.exe` (один файл; privacy-scan без личных путей сборщика).

Dev без установщика: `.\Install-Dev.ps1` / `.\Uninstall-Dev.ps1`.

## Лицензия

MIT — см. [LICENSE](LICENSE).

## Замечания

- Это **preview handler** для панели просмотра Проводника, не отдельное «приложение с окном».  
- После установки откройте Проводник и нажмите **Alt+P** на файле.  
- Антивирус может спросить при первом запуске неподписанного Setup — это ожидаемо, пока нет code signing.
