#requires -Version 7.4
[CmdletBinding()]
param([string]$ExampleDirectory, [switch]$Legacy)

$ErrorActionPreference = 'Stop'
$framework = if ($Legacy) { 'net8.0' } else { 'net10.0' }
$api = Join-Path $PSScriptRoot "DrivingSchool.Api/bin/Release/$framework/DrivingSchool.Api.dll"
if (-not (Test-Path -LiteralPath $api -PathType Leaf)) {
    throw 'Сначала соберите решение в конфигурации Release.'
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$scratch = Join-Path $temporaryRoot ('DrivingSchool.Smoke.' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$database = Join-Path $scratch 'smoke.db'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$baseUrl = "https://127.0.0.1:$port"
$script:checks = 0

if (-not ('DrivingSchoolSmokeTls' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public static class DrivingSchoolSmokeTls
{
    public static byte[] Certificate(string path, string password)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=DrivingSchool smoke", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        System.IO.File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return certificate.GetCertHash(HashAlgorithmName.SHA256);
    }

    public static HttpClient Client(byte[] expected)
    {
        var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
        handler.ServerCertificateCustomValidationCallback = (request, cert, _, _) =>
            request.RequestUri.Scheme == "https" && request.RequestUri.Host == "127.0.0.1" && cert != null &&
            CryptographicOperations.FixedTimeEquals(cert.GetCertHash(HashAlgorithmName.SHA256), expected);
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
    }
}
'@
}

function Confirm-Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "Не пройдена проверка: $Description" }
    $script:checks++
    Write-Host ('  {0,2}  {1}' -f $script:checks, $Description)
}

function Send-Request([string]$Method, [string]$Path, [int]$Expected,
    [string]$Token = '', $Body = $null, [switch]$Csv) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), "$baseUrl$Path")
    try {
        if ($Token) { $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token) }
        if ($null -ne $Body) {
            $type = if ($Csv) { 'text/csv' } else { 'application/json' }
            $payload = if ($Csv) { $Body } else { $Body | ConvertTo-Json -Depth 6 -Compress }
            $request.Content = [Net.Http.StringContent]::new($payload, [Text.Encoding]::UTF8, $type)
        }
        $response = $http.Send($request)
        try {
            $status = [int]$response.StatusCode
            Confirm-Check ($status -eq $Expected) "$Method $Path → $Expected (фактически $status)"
            return [pscustomobject]@{StatusCode = $status; Content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}
        }
        finally { $response.Dispose() }
    }
    finally { $request.Dispose() }
}

function Read-Json($Response) {
    ConvertFrom-Json -AsHashtable -NoEnumerate $Response.Content
}

function Get-DemoPassword([string]$Account) {
    $variable = "SCHOOL_DEMO_${Account}_PASSWORD"
    $value = [Environment]::GetEnvironmentVariable($variable)
    if ([string]::IsNullOrWhiteSpace($value)) { return 'Auto16-' + $Account.ToLowerInvariant() + '!' }
    return $value
}

function Open-Session([string]$Login, [string]$Password, [string]$Role) {
    $session = Read-Json (Send-Request POST /api/auth/login 200 -Body @{login=$Login; password=$Password})
    Confirm-Check ($session.user.role -eq $Role -and $session.user.login -eq $Login) "роль $Login = $Role"
    return $session.token
}

function Set-Stage([int]$Lesson, [string]$Token, [string]$Status, [int]$Version, [int]$Expected, [string]$Reason = '', $FinishedAt = $null) {
    Send-Request POST "/api/lessons/$Lesson/transition" $Expected $Token @{
        status=$Status; version=$Version; reason=$Reason; finishedAt=$FinishedAt
    } | Out-Null
}

$process = $null
$http = $null
try {
    $pfx = Join-Path $scratch 'smoke.pfx'
    $certificatePassword = [Guid]::NewGuid().ToString('N')
    $http = [DrivingSchoolSmokeTls]::Client([DrivingSchoolSmokeTls]::Certificate($pfx, $certificatePassword))
    $process = Start-Process dotnet -PassThru -WindowStyle Hidden -ArgumentList @(
        ('"' + $api + '"'), '--urls', $baseUrl, ('"--School:Database=' + $database + '"')
    ) -Environment @{
        Kestrel__Certificates__Default__Path = $pfx
        Kestrel__Certificates__Default__Password = $certificatePassword
    } -RedirectStandardOutput (Join-Path $scratch 'stdout.txt') -RedirectStandardError (Join-Path $scratch 'stderr.txt')

    $ready = $false
    $timeout = [Diagnostics.Stopwatch]::StartNew()
    while (-not $ready -and -not $process.HasExited -and $timeout.Elapsed.TotalSeconds -lt 25) {
        try {
            $probe = $http.GetAsync("$baseUrl/health").GetAwaiter().GetResult()
            $ready = $probe.IsSuccessStatusCode
            $probe.Dispose()
        }
        catch { Start-Sleep -Milliseconds 220 }
    }
    Confirm-Check ($ready -and (Test-Path -LiteralPath $database -PathType Leaf)) 'живой API и новая SQLite БД'
    Send-Request GET /api/lessons 401 | Out-Null
    Send-Request POST /api/auth/login 401 -Body @{login='admin'; password='invalid'} | Out-Null
    $admin = Open-Session admin (Get-DemoPassword ADMIN) Admin
    $manager = Open-Session manager (Get-DemoPassword MANAGER) Manager
    $one = Open-Session nikita (Get-DemoPassword NIKITA) Instructor
    $two = Open-Session sergey (Get-DemoPassword SERGEY) Instructor

    $users = Read-Json (Send-Request GET /api/users 200 $admin)
    Confirm-Check ($users.Count -eq 4) 'четыре демо-пользователя'
    Send-Request GET /api/users 403 $manager | Out-Null
    Send-Request GET /api/students 403 $one | Out-Null
    Send-Request GET /api/lessons/export/csv 403 $two | Out-Null
    $instructors = Read-Json (Send-Request GET /api/users/instructors 200 $manager)
    $instructor1 = ($instructors | Where-Object login -eq 'nikita').id
    $instructor2 = ($instructors | Where-Object login -eq 'sergey').id

    $empty = Read-Json (Send-Request GET /api/lessons/statistics 200 $manager)
    Confirm-Check ($empty.total -eq 0 -and $null -eq $empty.averageDays) 'пустая БД и средний срок без деления на ноль'
    $import = Read-Json (Send-Request POST /api/students/import 200 $manager -Csv -Body (
        "Name;Phone;Email" + [Environment]::NewLine + '"Орлова, Анна";+79990001234;anna@example.com'))
    Confirm-Check ($import.imported -eq 1) 'импорт ученика с кавычками и кириллицей'
    Send-Request POST /api/students/import 400 $manager -Csv -Body "Name;Phone;Email`nКорректная;100;`nОшибка;;" | Out-Null
    Send-Request POST /api/students/import 403 $one -Csv -Body 'Name,Phone,Email' | Out-Null
    $students = Read-Json (Send-Request GET /api/students 200 $manager)
    Confirm-Check ($students.Count -eq 1 -and $students[0].name -eq 'Орлова, Анна') 'ошибочный импорт не сохраняет часть строк'

    $body = @{studentId=$students[0].id; title='Городской маршрут'; address='Ул. Лесная, 14'}
    $first = (Read-Json (Send-Request POST /api/lessons 201 $manager -Body $body)).id
    $body.title = 'Парковка'
    $body.instructorId = $instructor2
    $second = (Read-Json (Send-Request POST /api/lessons 201 $admin -Body $body)).id
    Send-Request POST /api/lessons 403 $one -Body $body | Out-Null

    Set-Stage $first $manager Completed 1 400
    Set-Stage $first $manager Confirmed 1 400
    $body.title = 'Городской маршрут'
    $body.instructorId = $instructor1
    $body.version = 1
    $body.scheduledAt = [DateTimeOffset]::UtcNow.ToString('O')
    Send-Request PUT "/api/lessons/$first" 204 $manager -Body $body | Out-Null
    Set-Stage $first $manager Confirmed 1 409

    $firstList = Read-Json (Send-Request GET /api/lessons 200 $one)
    $secondList = Read-Json (Send-Request GET /api/lessons 200 $two)
    Confirm-Check ($firstList.Count -eq 1 -and $firstList[0].id -eq $first) 'инструктор 1 видит своё занятие'
    Confirm-Check ($secondList.Count -eq 1 -and $secondList[0].id -eq $second) 'инструктор 2 видит своё занятие'
    Send-Request GET "/api/lessons/$second" 404 $one | Out-Null
    Send-Request PUT "/api/lessons/$first" 403 $one -Body $body | Out-Null
    Set-Stage $first $two Confirmed 2 404
    Set-Stage $first $one Confirmed 2 204
    Set-Stage $first $one Completed 3 400
    $body.version = 3
    $body.amount = 1850.50
    Send-Request PUT "/api/lessons/$first" 204 $manager -Body $body | Out-Null
    Set-Stage $first $one Completed 4 400
    Set-Stage $first $one Completed 4 204 '' ([DateTimeOffset]::UtcNow.ToString('O'))
    Set-Stage $first $one Rescheduled 5 400 'Закрытое занятие'
    Set-Stage $second $two Rescheduled 1 400 '  '
    Set-Stage $second $two Rescheduled 1 204 'Ученик попросил перенос'
    $rescheduled = Read-Json (Send-Request GET "/api/lessons/$second" 200 $manager)
    Confirm-Check ($rescheduled.closingReason -eq 'Ученик попросил перенос') 'причина переноса сохранена'

    $stats = Read-Json (Send-Request GET /api/lessons/statistics 200 $manager)
    Confirm-Check ($stats.total -eq 2 -and $stats.completed -eq 1 -and $stats.rescheduled -eq 1 -and
        $stats.completedAmount -eq 1850.5 -and $null -ne $stats.averageDays -and $stats.byStatus.Count -eq 5) 'статистика всей автошколы'
    $ownStats = Read-Json (Send-Request GET /api/lessons/statistics 200 $two)
    Confirm-Check ($ownStats.total -eq 1 -and $ownStats.rescheduled -eq 1) 'личная статистика инструктора'

    $query = '?status=Completed&q=' + [Uri]::EscapeDataString('городской')
    $filter = Read-Json (Send-Request GET "/api/lessons$query" 200 $manager)
    Confirm-Check ($filter.Count -eq 1 -and $filter[0].id -eq $first) 'фильтр статуса и поиск без учёта регистра в кириллице'
    $csv = Send-Request GET "/api/lessons/export/csv$query" 200 $manager
    Confirm-Check ($csv.Content -match 'Городской' -and $csv.Content -notmatch 'Парковка') 'содержимое отфильтрованного CSV'
    $json = Read-Json (Send-Request GET "/api/lessons/export/json$query" 200 $manager)
    Confirm-Check ($json.Count -eq 1 -and $json[0].id -eq $first) 'содержимое отфильтрованного JSON'
    [xml]$xml = (Send-Request GET "/api/lessons/export/xml$query" 200 $manager).Content
    Confirm-Check (@($xml.Lessons.Lesson).Count -eq 1 -and $xml.Lessons.Lesson.Id -eq $first) 'содержимое отфильтрованного XML'
    Send-Request POST /api/auth/logout 204 $one | Out-Null
    Send-Request GET /api/auth/me 401 $one | Out-Null

    if ($ExampleDirectory) {
        $output = [IO.Path]::GetFullPath($ExampleDirectory)
        [IO.Directory]::CreateDirectory($output) | Out-Null
        [IO.File]::WriteAllText((Join-Path $output 'lessons-demo.csv'), $csv.Content)
        [IO.File]::WriteAllText((Join-Path $output 'lessons-demo.json'), ($json | ConvertTo-Json -Depth 6 -AsArray))
        $xml.Save((Join-Path $output 'lessons-demo.xml'))
    }
    Write-Host "Выполнено проверок живого API: $script:checks"
}
catch {
    $stderr = Join-Path $scratch 'stderr.txt'
    if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr }
    throw
}
finally {
    if ($null -ne $http) { $http.Dispose() }
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit()
    }
    $resolved = (Resolve-Path -LiteralPath $scratch).Path
    if ([IO.Path]::GetDirectoryName($resolved) -eq $temporaryRoot.TrimEnd('\', '/') -and
        [IO.Path]::GetFileName($resolved).StartsWith('DrivingSchool.Smoke.')) {
        Remove-Item -LiteralPath $resolved -Recurse
    }
}

