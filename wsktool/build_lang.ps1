# build_lang.ps1 - 生成 wsktool 多语言资源 DLL
$ErrorActionPreference = "Stop"
$progName = "wsktool"
$outDir = "E:\WSK_Tools\language"
$rcExe = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\rc.exe"
$linkExe = "E:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\link.exe"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$strings = @(
    @(100,"wsktool - WSK 构建工具","wsktool - WSK 建構工具","wsktool - WSK Build Tool","wsktool - WSK ビルドツール","wsktool - Инструмент сборки WSK","wsktool - WSK 빌드 도구"),
    @(101,"WSK 路径:","WSK 路徑:","WSK Path:","WSK パス:","Путь WSK:","WSK 경로:"),
    @(102,"浏览...","瀏覽...","Browse...","参照...","Обзор...","찾아보기..."),
    @(103,"自动检测","自動偵測","Auto Detect","自動検出","Автоопределение","자동 감지"),
    @(104,"工作区:","工作區:","Workspace:","ワークスペース:","Рабочая область:","작업 공간:"),
    @(105,"选择...","選擇...","Select...","選択...","Выбрать...","선택..."),
    @(106,"架构:","架構:","Architecture:","アーキテクチャ:","Архитектура:","아키텍처:"),
    @(107,"SKU:","SKU:","SKU:","SKU:","SKU:","SKU:"),
    @(108,"实体机","實體機","Physical Machine","物理マシン","Физическая машина","물리적 머신"),
    @(109,"虚拟机","虛擬機","Virtual Machine","仮想マシン","Виртуальная машина","가상 머신"),
    @(110,"开始构建","開始建構","Start Build","ビルド開始","Начать сборку","빌드 시작"),
    @(111,"输出日志:","輸出日誌:","Output Log:","出力ログ:","Журнал вывода:","출력 로그:"),
    @(112,"就绪","就緒","Ready","準備完了","Готово","준비"),
    @(113,"请选择 WSK 路径","請選擇 WSK 路徑","Please select WSK path","WSK パスを選択してください","Выберите путь WSK","WSK 경로를 선택하세요"),
    @(114,"请选择工作区目录","請選擇工作區目錄","Please select workspace directory","ワークスペースディレクトリを選択してください","Выберите каталог рабочей области","작업 공간 디렉터리를 선택하세요"),
    @(115,"请选择架构","請選擇架構","Please select architecture","アーキテクチャを選択してください","Выберите архитектуру","아키텍처를 선택하세요"),
    @(116,"请选择 SKU","請選擇 SKU","Please select SKU","SKU を選択してください","Выберите SKU","SKU를 선택하세요"),
    @(117,"WSK 路径无效","WSK 路徑無效","Invalid WSK path","WSK パスが無効です","Неверный путь WSK","WSK 경로가 잘못됨"),
    @(118,"工作区目录不存在","工作區目錄不存在","Workspace directory does not exist","ワークスペースディレクトリが存在しません","Каталог рабочей области не существует","작업 공간 디렉터리가 존재하지 않음"),
    @(119,"工作区目录不存在, 是否创建?","工作區目錄不存在, 是否建立?","Workspace dir does not exist, create?","ワークスペースディレクトリが存在しません、作成しますか?","Каталог рабочей области не существует, создать?","작업 공간 디렉터리가 없습니다. 생성하시겠습니까?"),
    @(120,"wcos在ARM/ARM64作为目标体系的情况下需要额外的设备布局，请确定你的oeminput已经添加了该内容！","wcos在ARM/ARM64作為目標體系的情況下需要額外的裝置佈局，請確定你的oeminput已經添加了該內容！","WCOS on ARM/ARM64 requires additional device layout. Please ensure your OEMInput includes it!","ARM/ARM64をターゲットとするWCOSには追加のデバイスレイアウトが必要です。OEMInputに含まれていることを確認してください！","Для WCOS на ARM/ARM64 требуется дополнительная разметка устройства. Убедитесь, что это добавлено в OEMInput!","ARM/ARM64를 대상으로 하는 WCOS에는 추가 장치 레이아웃이 필요합니다. OEMInput에 포함되었는지 확인하세요!"),
    @(121,"架构提醒","架構提醒","Architecture Warning","アーキテクチャ警告","Предупреждение об архитектуре","아키텍처 경고"),
    @(122,"正在构建 WSK 映像...","正在建構 WSK 映像...","Building WSK image...","WSK イメージをビルド中...","Сборка образа WSK...","WSK 이미지 빌드 중..."),
    @(123,"WSK 构建成功","WSK 建構成功","WSK Build Succeeded","WSK ビルド成功","Сборка WSK успешна","WSK 빌드 성공"),
    @(124,"WSK 构建失败","WSK 建構失敗","WSK Build Failed","WSK ビルド失敗","Сборка WSK не удалась","WSK 빌드 실패"),
    @(125,"WSK 构建成功！","WSK 建構成功！","WSK build succeeded!","WSK ビルドに成功しました！","Сборка WSK успешно завершена!","WSK 빌드 성공!"),
    @(126,"完成","完成","Done","完了","Готово","완료"),
    @(127,"WSK 构建失败！","WSK 建構失敗！","WSK build failed!","WSK ビルドに失敗しました！","Сборка WSK не удалась!","WSK 빌드 실패!"),
    @(128,"失败","失敗","Failed","失敗","Ошибка","실패"),
    @(129,"=== wsktool - WSK 构建工具 ===","=== wsktool - WSK 建構工具 ===","=== wsktool - WSK Build Tool ===","=== wsktool - WSK ビルドツール ===","=== wsktool - Инструмент сборки WSK ===","=== wsktool - WSK 빌드 도구 ==="),
    @(130,"已自动检测到 WSK: ","已自動偵測到 WSK: ","Auto-detected WSK: ","WSK を自動検出: ","WSK определен автоматически: ","WSK 자동 감지: "),
    @(131,"未检测到 WSK, 请手动选择","未偵測到 WSK, 請手動選擇","WSK not detected, please select manually","WSK が検出されません、手動で選択してください","WSK не обнаружен, выберите вручную","WSK가 감지되지 않음, 수동으로 선택하세요"),
    @(132,"选择 OEMInput XML","選擇 OEMInput XML","Select OEMInput XML","OEMInput XML を選択","Выберите OEMInput XML","OEMInput XML 선택"),
    @(133,"已选择 XML: ","已選擇 XML: ","Selected XML: ","選択した XML: ","Выбран XML: ","선택된 XML: "),
    @(134,"未找到可用的 XML 文件","未找到可用的 XML 檔案","No XML files found","利用可能な XML ファイルが見つかりません","Доступные XML-файлы не найдены","사용 가능한 XML 파일을 찾을 수 없음"),
    @(135,"=== WSK 构建开始 ===","=== WSK 建構開始 ===","=== WSK Build Started ===","=== WSK ビルド開始 ===","=== Сборка WSK начата ===","=== WSK 빌드 시작 ==="),
    @(136,"=== WSK 构建成功 ===","=== WSK 建構成功 ===","=== WSK Build Succeeded ===","=== WSK ビルド成功 ===","=== Сборка WSK успешна ===","=== WSK 빌드 성공 ==="),
    @(137,"=== WSK 构建失败 ===","=== WSK 建構失敗 ===","=== WSK Build Failed ===","=== WSK ビルド失敗 ===","=== Сборка WSK не удалась ===","=== WSK 빌드 실패 ==="),
    @(138,"退出码: 0x","退出碼: 0x","Exit code: 0x","終了コード: 0x","Код выхода: 0x","종료 코드: 0x"),
    @(139,"输出目录: ","輸出目錄: ","Output dir: ","出力ディレクトリ: ","Выходной каталог: ","출력 디렉터리: "),
    @(140,"构建正在进行中...","建構正在進行中...","Build in progress...","ビルド進行中...","Идет сборка...","빌드 진행 중..."),
    @(141,"语言","語言","Language","言語","Язык","언어"),
    @(142,"切换语言...","切換語言...","Switch Language...","言語を切り替え...","Сменить язык...","언어 전환..."),
    @(143,"帮助","說明","Help","ヘルプ","Справка","도움말"),
    @(144,"关于...","關於...","About...","バージョン情報...","О программе...","정보...")
)

$langCodes = @("zh-cn","zh-tw","en-us","ja-jp","ru-ru","ko-kr")
$langIdx = @{"zh-cn"=0;"zh-tw"=1;"en-us"=2;"ja-jp"=3;"ru-ru"=4;"ko-kr"=5}
$tempDir = Join-Path $env:TEMP "wsktool_lang_build"
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
