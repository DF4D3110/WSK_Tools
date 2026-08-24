# build_lang.ps1 - 生成 ffuext 多语言资源 DLL
$ErrorActionPreference = "Stop"
$progName = "ffuext"
$outDir = "E:\WSK_Tools\language"
$rcExe = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\rc.exe"
$linkExe = "E:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\link.exe"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

# @(id, zh-cn, zh-tw, en-us, ja-jp, ru-ru, ko-kr)
$strings = @(
    @(100, "ffuext - FFU 镜像释放工具", "ffuext - FFU 鏡像釋放工具", "ffuext - FFU Image Apply Tool", "ffuext - FFU イメージ適用ツール", "ffuext - Инструмент применения FFU", "ffuext - FFU 이미지 적용 도구"),
    @(101, "FFU 文件:", "FFU 檔案:", "FFU File:", "FFU ファイル:", "Файл FFU:", "FFU 파일:"),
    @(102, "浏览...", "瀏覽...", "Browse...", "参照...", "Обзор...", "찾아보기..."),
    @(103, "目标磁盘:", "目標磁碟:", "Target Disk:", "ターゲットディスク:", "Целевой диск:", "대상 디스크:"),
    @(104, "刷新", "重新整理", "Refresh", "更新", "Обновить", "새로고침"),
    @(105, "开始释放", "開始釋放", "Start Apply", "適用開始", "Начать применение", "적용 시작"),
    @(106, "输出日志:", "輸出日誌:", "Output Log:", "出力ログ:", "Журнал вывода:", "출력 로그:"),
    @(107, "就绪", "就緒", "Ready", "準備完了", "Готово", "준비"),
    @(108, "请先选择 FFU 文件", "請先選擇 FFU 檔案", "Please select FFU file first", "最初に FFU ファイルを選択してください", "Сначала выберите файл FFU", "먼저 FFU 파일을 선택하세요"),
    @(109, "FFU 文件不存在", "FFU 檔案不存在", "FFU file does not exist", "FFU ファイルが存在しません", "Файл FFU не существует", "FFU 파일이 존재하지 않습니다"),
    @(110, "请选择目标磁盘", "請選擇目標磁碟", "Please select target disk", "ターゲットディスクを選択してください", "Выберите целевой диск", "대상 디스크를 선택하세요"),
    @(111, "无效的磁盘选择", "無效的磁碟選擇", "Invalid disk selection", "無効なディスク選択", "Неверный выбор диска", "잘못된 디스크 선택"),
    @(112, "确认操作", "確認操作", "Confirm Operation", "操作の確認", "Подтверждение операции", "작업 확인"),
    @(113, "即将把 FFU 镜像释放到 PhysicalDrive", "即將把 FFU 鏡像釋放到 PhysicalDrive", "About to apply FFU image to PhysicalDrive", "FFU イメージを PhysicalDrive に適用します", "Применение образа FFU к PhysicalDrive", "FFU 이미지를 PhysicalDrive에 적용합니다"),
    @(114, "目标磁盘上的所有数据将被永久覆盖!", "目標磁碟上的所有資料將被永久覆蓋!", "All data on the target disk will be permanently overwritten!", "ターゲットディスク上のすべてのデータは完全に上書きされます!", "Все данные на целевом диске будут безвозвратно перезаписаны!", "대상 디스의 모든 데이터가 영구적으로 덮어씁니다!"),
    @(115, "是否继续?", "是否繼續?", "Continue?", "続行しますか?", "Продолжить?", "계속하시겠습니까?"),
    @(116, "权限不足", "權限不足", "Insufficient Permissions", "権限が不足しています", "Недостаточно прав", "권한 부족"),
    @(117, "释放 FFU 到物理磁盘需要管理员权限。请右键以管理员身份重新运行本程序。", "釋放 FFU 到實體磁碟需要系統管理員權限。請右鍵以系統管理員身分重新執行本程式。", "Applying FFU to a physical disk requires administrator privileges. Please right-click and run as administrator.", "物理ディスクへの FFU 適用には管理者権限が必要です。右クリックして管理者として実行してください。", "Применение FFU к физическому диску требует прав администратора. Нажмите правой кнопкой и запустите от имени администратора.", "물리적 디스크에 FFU 적용에는 관리자 권한이 필요합니다. 마우스 오른쪽 버튼으로 관리자 권한으로 실행하세요."),
    @(118, "正在释放 FFU 镜像...", "正在釋放 FFU 鏡像...", "Applying FFU image...", "FFU イメージを適用中...", "Применение образа FFU...", "FFU 이미지 적용 중..."),
    @(119, "=== FFU 释放开始 ===", "=== FFU 釋放開始 ===", "=== FFU Apply Started ===", "=== FFU 適用開始 ===", "=== Применение FFU начато ===", "=== FFU 적용 시작 ==="),
    @(120, "FFU 文件: ", "FFU 檔案: ", "FFU File: ", "FFU ファイル: ", "Файл FFU: ", "FFU 파일: "),
    @(121, "目标磁盘: PhysicalDrive", "目標磁碟: PhysicalDrive", "Target Disk: PhysicalDrive", "ターゲットディスク: PhysicalDrive", "Целевой диск: PhysicalDrive", "대상 디스크: PhysicalDrive"),
    @(122, "无法创建工作线程", "無法建立工作執行緒", "Failed to create worker thread", "ワーカースレッドの作成に失敗しました", "Не удалось создать рабочий поток", "작업 스레드 생성 실패"),
    @(123, "错误", "錯誤", "Error", "エラー", "Ошибка", "오류"),
    @(124, "提示", "提示", "Prompt", "プロンプト", "Подсказка", "알림"),
    @(125, "=== ffuext - FFU 镜像释放工具 ===", "=== ffuext - FFU 鏡像釋放工具 ===", "=== ffuext - FFU Image Apply Tool ===", "=== ffuext - FFU イメージ適用ツール ===", "=== ffuext - Инструмент применения FFU ===", "=== ffuext - FFU 이미지 적용 도구 ==="),
    @(126, "架构: ", "架構: ", "Architecture: ", "アーキテクチャ: ", "Архитектура: ", "아키텍처: "),
    @(127, "权限: 已提升 (管理员)", "權限: 已提升 (系統管理員)", "Permissions: Elevated (Administrator)", "権限: 昇格済み (管理者)", "Права: повышены (Администратор)", "권한: 상승됨 (관리자)"),
    @(128, "权限: 未提升 (释放 FFU 需要管理员身份运行)", "權限: 未提升 (釋放 FFU 需要系統管理員身分執行)", "Permissions: Not elevated (Run as administrator required to apply FFU)", "権限: 未昇格 (FFU 適用には管理者実行が必要)", "Права: не повышены (требуется запуск от имени администратора)", "권한: 상승되지 않음 (FFU 적용에는 관리자 권한 필요)"),
    @(129, "正在检测 DISM /Apply-Ffu 支持...", "正在偵測 DISM /Apply-Ffu 支援...", "Detecting DISM /Apply-Ffu support...", "DISM /Apply-Ffu サポートを検出中...", "Определение поддержки DISM /Apply-Ffu...", "DISM /Apply-Ffu 지원 감지 중..."),
    @(130, "DISM 版本: ", "DISM 版本: ", "DISM Version: ", "DISM バージョン: ", "Версия DISM: ", "DISM 버전: "),
    @(131, "DISM /Apply-Ffu: 支持", "DISM /Apply-Ffu: 支援", "DISM /Apply-Ffu: Supported", "DISM /Apply-Ffu: サポートあり", "DISM /Apply-Ffu: поддерживается", "DISM /Apply-Ffu: 지원됨"),
    @(132, "DISM /Apply-Ffu: 不支持!", "DISM /Apply-Ffu: 不支援!", "DISM /Apply-Ffu: Not supported!", "DISM /Apply-Ffu: サポートなし!", "DISM /Apply-Ffu: не поддерживается!", "DISM /Apply-Ffu: 지원되지 않음!"),
    @(133, "提示: /Apply-Ffu 需要 Windows 10 1709+ 或 Windows 11 的 DISM 版本", "提示: /Apply-Ffu 需要 Windows 10 1709+ 或 Windows 11 的 DISM 版本", "Tip: /Apply-Ffu requires DISM from Windows 10 1709+ or Windows 11", "ヒント: /Apply-Ffu には Windows 10 1709+ または Windows 11 の DISM が必要です", "Совет: /Apply-Ffu требует DISM из Windows 10 1709+ или Windows 11", "팁: /Apply-Ffu에는 Windows 10 1709+ 또는 Windows 11의 DISM이 필요합니다"),
    @(134, "就绪 - DISM 支持 FFU 释放", "就緒 - DISM 支援 FFU 釋放", "Ready - DISM supports FFU apply", "準備完了 - DISM は FFU 適用をサポート", "Готово - DISM поддерживает применение FFU", "준비 - DISM이 FFU 적용 지원"),
    @(135, "警告 - 当前 DISM 不支持 FFU 释放", "警告 - 目前 DISM 不支援 FFU 釋放", "Warning - Current DISM does not support FFU apply", "警告 - 現在の DISM は FFU 適用をサポートしません", "Предупреждение - текущий DISM не поддерживает применение FFU", "경고 - 현재 DISM이 FFU 적용을 지원하지 않음"),
    @(136, "已枚举 个物理磁盘", "已列舉 個實體磁碟", " physical disks enumerated", " 個の物理ディスクを列挙しました", " физических дисков перечислено", "개의 물리적 디스크 열거됨"),
    @(137, "磁盘列表已刷新", "磁碟清單已重新整理", "Disk list refreshed", "ディスク一覧を更新しました", "Список дисков обновлен", "디스크 목록 새로고침됨"),
    @(138, "正在释放... ", "正在釋放... ", "Applying... ", "適用中... ", "Применение... ", "적용 중... "),
    @(139, "=== FFU 释放成功 ===", "=== FFU 釋放成功 ===", "=== FFU Apply Succeeded ===", "=== FFU 適用成功 ===", "=== Применение FFU успешно ===", "=== FFU 적용 성공 ==="),
    @(140, "完成 - FFU 释放成功", "完成 - FFU 釋放成功", "Done - FFU apply succeeded", "完了 - FFU 適用成功", "Готово - применение FFU успешно", "완료 - FFU 적용 성공"),
    @(141, "FFU 镜像释放成功!", "FFU 鏡像釋放成功!", "FFU image applied successfully!", "FFU イメージの適用に成功しました!", "Образ FFU успешно применен!", "FFU 이미지 적용 성공!"),
    @(142, "完成", "完成", "Done", "完了", "Готово", "완료"),
    @(143, "=== FFU 释放失败 ===", "=== FFU 釋放失敗 ===", "=== FFU Apply Failed ===", "=== FFU 適用失敗 ===", "=== Применение FFU не удалось ===", "=== FFU 적용 실패 ==="),
    @(144, "退出码: 0x", "退出碼: 0x", "Exit code: 0x", "終了コード: 0x", "Код выхода: 0x", "종료 코드: 0x"),
    @(145, "错误信息:", "錯誤資訊:", "Error message:", "エラーメッセージ:", "Сообщение об ошибке:", "오류 메시지:"),
    @(146, "失败 - 退出码 0x", "失敗 - 退出碼 0x", "Failed - Exit code 0x", "失敗 - 終了コード 0x", "Ошибка - код выхода 0x", "실패 - 종료 코드 0x"),
    @(147, "FFU 释放失败!", "FFU 釋放失敗!", "FFU apply failed!", "FFU の適用に失敗しました!", "Применение FFU не удалось!", "FFU 적용 실패!"),
    @(148, "失败", "失敗", "Failed", "失敗", "Ошибка", "실패"),
    @(149, "(未检测到物理磁盘)", "(未偵測到實體磁碟)", "(No physical disks detected)", "(物理ディスクが検出されません)", "(физические диски не обнаружены)", "(물리적 디스크가 감지되지 않음)"),
    @(150, "(大小未知)", "(大小未知)", "(Unknown size)", "(サイズ不明)", "(неизвестный размер)", "(크기 알 수 없음)"),
    @(151, "[需管理员权限]", "[需系統管理員權限]", "[Admin required]", "[管理者権限が必要]", "[требуются права администратора]", "[관리자 권한 필요]"),
    @(152, "语言", "語言", "Language", "言語", "Язык", "언어"),
    @(153, "切换语言...", "切換語言...", "Switch Language...", "言語を切り替え...", "Сменить язык...", "언어 전환..."),
    @(154, "帮助", "說明", "Help", "ヘルプ", "Справка", "도움말"),
    @(155, "关于...", "關於...", "About...", "バージョン情報...", "О программе...", "정보..."),
    @(156, "FFU 镜像文件 (*.ffu)", "FFU 鏡像檔案 (*.ffu)", "FFU Image Files (*.ffu)", "FFU イメージファイル (*.ffu)", "Файлы образов FFU (*.ffu)", "FFU 이미지 파일 (*.ffu)"),
    @(157, "所有文件 (*.*)", "所有檔案 (*.*)", "All Files (*.*)", "すべてのファイル (*.*)", "Все файлы (*.*)", "모든 파일 (*.*)"),
    @(158, "选择 FFU 镜像文件", "選擇 FFU 鏡像檔案", "Select FFU Image File", "FFU イメージファイルを選択", "Выберите файл образа FFU", "FFU 이미지 파일 선택")
)

$langCodes = @("zh-cn", "zh-tw", "en-us", "ja-jp", "ru-ru", "ko-kr")
$langIdx = @{ "zh-cn" = 0; "zh-tw" = 1; "en-us" = 2; "ja-jp" = 3; "ru-ru" = 4; "ko-kr" = 5 }
$tempDir = Join-Path $env:TEMP "ffuext_lang_build"
if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

foreach ($lang in $langCodes) {
    $idx = $langIdx[$lang]
    $rcFile = Join-Path $tempDir "$progName`_$lang.rc"
    $resFile = Join-Path $tempDir "$progName`_$lang.res"
    $dllFile = Join-Path $outDir "$progName`_$lang.dll"
    $rcContent = "STRINGTABLE`r`nBEGIN`r`n"
    foreach ($entry in $strings) {
        $id = $entry[0]; $val = $entry[$idx + 1]
        $escaped = $val -replace '"', '""'
        $rcContent += "    $id `"$escaped`"`r`n"
    }
    $rcContent += "END`r`n"
    [System.IO.File]::WriteAllText($rcFile, $rcContent, [System.Text.Encoding]::Unicode)
    Write-Host "Compiling $lang ..." -ForegroundColor Cyan
    & $rcExe /nologo /fo $resFile $rcFile 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host "RC failed for $lang" -ForegroundColor Red; continue }
    & $linkExe /nologo /dll /noentry /machine:x64 /out:$dllFile $resFile 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host "LINK failed for $lang" -ForegroundColor Red; continue }
    Write-Host "  -> $dllFile" -ForegroundColor Green
}
Remove-Item -Recurse -Force $tempDir
Write-Host "`nDone!" -ForegroundColor Green
