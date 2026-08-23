# build_lang.ps1 - 生成 wcosstagetool 多语言资源 DLL
# 用法: powershell -ExecutionPolicy Bypass -File build_lang.ps1

$ErrorActionPreference = "Stop"

$progName = "wcosstagetool"
$outDir = "E:\WSK_Tools\language"
$rcExe = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\rc.exe"
$linkExe = "E:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\link.exe"

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

# 字符串表: @(id, zh-cn, zh-tw, en-us, ja-jp, ru-ru, ko-kr)
$strings = @(
    @(100, "wcosstagetool - WCOS 阶段工具", "wcosstagetool - WCOS 階段工具", "wcosstagetool - WCOS Stage Tool", "wcosstagetool - WCOS ステージツール", "wcosstagetool - Инструмент этапов WCOS", "wcosstagetool - WCOS 단계 도구"),
    @(101, "wcos构建", "wcos建構", "wcos Build", "wcos ビルド", "Сборка WCOS", "wcos 빌드"),
    @(102, "驱动注入", "驅動注入", "Driver Inject", "ドライバー注入", "Внедрение драйверов", "드라이버 주입"),
    @(103, "cab注入", "cab注入", "CAB Inject", "CAB 注入", "Внедрение CAB", "CAB 주입"),
    @(104, "bcd可选", "bcd可選", "BCD Options", "BCD オプション", "Параметры BCD", "BCD 옵션"),
    @(105, "开始", "開始", "Start", "開始", "Начать", "시작"),
    @(106, "浏览...", "瀏覽...", "Browse...", "参照...", "Обзор...", "찾아보기..."),
    @(107, "选择...", "選擇...", "Select...", "選択...", "Выбрать...", "선택..."),
    @(108, "imggen.cmd:", "imggen.cmd:", "imggen.cmd:", "imggen.cmd:", "imggen.cmd:", "imggen.cmd:"),
    @(109, "输出目录:", "輸出目錄:", "Output Dir:", "出力ディレクトリ:", "Выходной каталог:", "출력 디렉터리:"),
    @(110, "FFU 文件名:", "FFU 檔名:", "FFU Filename:", "FFU ファイル名:", "Имя файла FFU:", "FFU 파일 이름:"),
    @(111, "OEMInput XML:", "OEMInput XML:", "OEMInput XML:", "OEMInput XML:", "OEMInput XML:", "OEMInput XML:"),
    @(112, "MSPackage 根目录:", "MSPackage 根目錄:", "MSPackage Root:", "MSPackage ルート:", "Корень MSPackage:", "MSPackage 루트:"),
    @(113, "CPU 类型:", "CPU 類型:", "CPU Type:", "CPU タイプ:", "Тип CPU:", "CPU 유형:"),
    @(114, "imageapp.exe:", "imageapp.exe:", "imageapp.exe:", "imageapp.exe:", "imageapp.exe:", "imageapp.exe:"),
    @(115, "FFU 文件:", "FFU 檔案:", "FFU File:", "FFU ファイル:", "Файл FFU:", "FFU 파일:"),
    @(116, "驱动目录:", "驅動目錄:", "Driver Dir:", "ドライバーディレクトリ:", "Каталог драйверов:", "드라이버 디렉터리:"),
    @(117, "UpdateApp.exe:", "UpdateApp.exe:", "UpdateApp.exe:", "UpdateApp.exe:", "UpdateApp.exe:", "UpdateApp.exe:"),
    @(118, "VHD 文件:", "VHD 檔案:", "VHD File:", "VHD ファイル:", "Файл VHD:", "VHD 파일:"),
    @(119, "CAB 文件夹:", "CAB 資料夾:", "CAB Folder:", "CAB フォルダ:", "Папка CAB:", "CAB 폴더:"),
    @(120, "BCD 文件:", "BCD 檔案:", "BCD File:", "BCD ファイル:", "Файл BCD:", "BCD 파일:"),
    @(121, "debug on", "debug on", "debug on", "debug on", "debug on", "debug on"),
    @(122, "debugtype serial", "debugtype serial", "debugtype serial", "debugtype serial", "debugtype serial", "debugtype serial"),
    @(123, "debugport 1", "debugport 1", "debugport 1", "debugport 1", "debugport 1", "debugport 1"),
    @(124, "baudrate 115200", "baudrate 115200", "baudrate 115200", "baudrate 115200", "baudrate 115200", "baudrate 115200"),
    @(125, "testsigning on", "testsigning on", "testsigning on", "testsigning on", "testsigning on", "testsigning on"),
    @(126, "nointegritychecks on", "nointegritychecks on", "nointegritychecks on", "nointegritychecks on", "nointegritychecks on", "nointegritychecks on"),
    @(127, "就绪", "就緒", "Ready", "準備完了", "Готово", "준비"),
    @(128, "正在执行...", "正在執行...", "Running...", "実行中...", "Выполнение...", "실행 중..."),
    @(129, "执行成功", "執行成功", "Success", "成功", "Успешно", "성공"),
    @(130, "执行失败", "執行失敗", "Failed", "失敗", "Ошибка", "실패"),
    @(131, "请选择 imggen.cmd", "請選擇 imggen.cmd", "Please select imggen.cmd", "imggen.cmd を選択してください", "Выберите imggen.cmd", "imggen.cmd를 선택하세요"),
    @(132, "请填写输出目录、OEMInput XML 和 MSPackage 根目录", "請填寫輸出目錄、OEMInput XML 和 MSPackage 根目錄", "Please fill Output Dir, OEMInput XML and MSPackage Root", "出力ディレクトリ、OEMInput XML、MSPackage ルートを入力してください", "Заполните выходной каталог, OEMInput XML и корень MSPackage", "출력 디렉터리, OEMInput XML, MSPackage 루트를 입력하세요"),
    @(133, "请选择 imageapp.exe", "請選擇 imageapp.exe", "Please select imageapp.exe", "imageapp.exe を選択してください", "Выберите imageapp.exe", "imageapp.exe를 선택하세요"),
    @(134, "请填写 FFU 路径、CPU 类型和驱动目录", "請填寫 FFU 路徑、CPU 類型和驅動目錄", "Please fill FFU path, CPU type and Driver dir", "FFU パス、CPU タイプ、ドライバーディレクトリを入力してください", "Заполните путь FFU, тип CPU и каталог драйверов", "FFU 경로, CPU 유형, 드라이버 디렉터리를 입력하세요"),
    @(135, "请选择 UpdateApp.exe", "請選擇 UpdateApp.exe", "Please select UpdateApp.exe", "UpdateApp.exe を選択してください", "Выберите UpdateApp.exe", "UpdateApp.exe를 선택하세요"),
    @(136, "请填写 VHD 路径和 CAB 文件夹路径", "請填寫 VHD 路徑和 CAB 資料夾路徑", "Please fill VHD path and CAB folder", "VHD パスと CAB フォルダを入力してください", "Заполните путь VHD и папку CAB", "VHD 경로와 CAB 폴더를 입력하세요"),
    @(137, "请选择 BCD 文件", "請選擇 BCD 檔案", "Please select BCD file", "BCD ファイルを選択してください", "Выберите файл BCD", "BCD 파일을 선택하세요"),
    @(138, "请至少勾选一个选项", "請至少勾選一個選項", "Please check at least one option", "少なくとも1つのオプションをチェックしてください", "Отметьте хотя бы одну опцию", "하나 이상의 옵션을 체크하세요"),
    @(139, "输出目录不存在, 是否创建?", "輸出目錄不存在, 是否建立?", "Output dir does not exist, create?", "出力ディレクトリが存在しません、作成しますか?", "Выходной каталог не существует, создать?", "출력 디렉터리가 없습니다. 생성하시겠습니까?"),
    @(140, "输出目录非空, 请选择 FFU 文件保存位置", "輸出目錄非空, 請選擇 FFU 檔案儲存位置", "Output dir is not empty, select FFU save location", "出力ディレクトリが空ではありません、FFU の保存場所を選択してください", "Выходной каталог не пуст, выберите место сохранения FFU", "출력 디렉터리가 비어있지 않습니다. FFU 저장 위치를 선택하세요"),
    @(141, "保存 FFU 文件", "儲存 FFU 檔案", "Save FFU File", "FFU ファイルを保存", "Сохранить файл FFU", "FFU 파일 저장"),
    @(142, "未找到语言文件 (language 目录)", "未找到語言檔案 (language 目錄)", "No language files found (language folder)", "言語ファイルが見つかりません (language フォルダ)", "Файлы языков не найдены (папка language)", "언어 파일을 찾을 수 없습니다 (language 폴더)"),
    @(143, "语言", "語言", "Language", "言語", "Язык", "언어"),
    @(144, "切换语言...", "切換語言...", "Switch Language...", "言語を切り替え...", "Сменить язык...", "언어 전환..."),
    @(145, "=== 执行成功 ===", "=== 執行成功 ===", "=== Success ===", "=== 成功 ===", "=== Успешно ===", "=== 성공 ==="),
    @(146, "=== 执行失败, 退出码: ===", "=== 執行失敗, 退出碼: ===", "=== Failed, exit code: ===", "=== 失敗, 終了コード: ===", "=== Ошибка, код выхода: ===", "=== 실패, 종료 코드: ==="),
    @(147, "已自动检测到工具路径", "已自動偵測到工具路徑", "Auto-detected tool path", "ツールパスを自動検出しました", "Путь к инструменту определен автоматически", "도구 경로를 자동으로 감지했습니다"),
    @(148, "未检测到工具路径, 请手动选择", "未偵測到工具路徑, 請手動選擇", "Tool path not detected, please select manually", "ツールパスが検出されません、手動で選択してください", "Путь к инструменту не определен, выберите вручную", "도구 경로를 감지하지 못했습니다. 수동으로 선택하세요"),
    @(149, "选择语言", "選擇語言", "Select Language", "言語を選択", "Выбор языка", "언어 선택"),
    @(150, "选择语言:", "選擇語言:", "Select Language:", "言語を選択:", "Выберите язык:", "언어를 선택하세요:"),
    @(151, "确定", "確定", "OK", "OK", "ОК", "확인"),
    @(152, "取消", "取消", "Cancel", "キャンセル", "Отмена", "취소"),
    @(153, "帮助", "說明", "Help", "ヘルプ", "Справка", "도움말"),
    @(154, "关于...", "關於...", "About...", "バージョン情報...", "О программе...", "정보...")
)

$langCodes = @("zh-cn", "zh-tw", "en-us", "ja-jp", "ru-ru", "ko-kr")
$langIdx = @{ "zh-cn" = 0; "zh-tw" = 1; "en-us" = 2; "ja-jp" = 3; "ru-ru" = 4; "ko-kr" = 5 }

$tempDir = Join-Path $env:TEMP "wcos_lang_build"
if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

foreach ($lang in $langCodes) {
    $idx = $langIdx[$lang]
    $rcFile = Join-Path $tempDir "$progName`_$lang.rc"
    $resFile = Join-Path $tempDir "$progName`_$lang.res"
    $dllFile = Join-Path $outDir "$progName`_$lang.dll"

    $rcContent = "STRINGTABLE`r`nBEGIN`r`n"
    foreach ($entry in $strings) {
        $id = $entry[0]
        $val = $entry[$idx + 1]
        $escaped = $val -replace '"', '""'
        $rcContent += "    $id `"$escaped`"`r`n"
    }
    $rcContent += "END`r`n"

    # .rc 文件必须用 UTF-16 LE 编码 (RC 编译器要求)
    [System.IO.File]::WriteAllText($rcFile, $rcContent, [System.Text.Encoding]::Unicode)

    Write-Host "Compiling $lang ..." -ForegroundColor Cyan
    & $rcExe /nologo /fo $resFile $rcFile 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host "RC failed for $lang" -ForegroundColor Red; continue }

    & $linkExe /nologo /dll /noentry /machine:x64 /out:$dllFile $resFile 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host "LINK failed for $lang" -ForegroundColor Red; continue }

    Write-Host "  -> $dllFile" -ForegroundColor Green
}

Remove-Item -Recurse -Force $tempDir
Write-Host "`nDone! Language DLLs in $outDir" -ForegroundColor Green
