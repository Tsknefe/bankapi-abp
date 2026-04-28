[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Scenario1", "Scenario2", "All")]
    [string]$Scenario,

    [Parameter(Mandatory = $true)]
    [string]$BearerToken,

    [Parameter(Mandatory = $true)]
    [string]$AccountA,

    [Parameter(Mandatory = $true)]
    [string]$AccountB,

    [string]$BaseUrl1 = "https://localhost:44389",
    [string]$BaseUrl2 = "https://localhost:44389",

    [decimal]$Scenario1AmountAB = 30,
    [decimal]$Scenario1AmountBA = 20,
    [decimal]$Scenario2WithdrawAmount = 80,

    [int]$CacheSettleDelayMs = 1200,
    [string]$LogPath = ".\distributed-test-log.txt"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Log {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )

    $line = "{0} [{1}] {2}" -f (Get-Date).ToString("o"), $Level, $Message
    $line | Tee-Object -FilePath $LogPath -Append | Out-Null
    Write-Host $line
}

function Wait-For-CacheSettle {
    param([string]$ScenarioName)

    Write-Log "$ScenarioName waiting $CacheSettleDelayMs ms for cache settle"
    Start-Sleep -Milliseconds $CacheSettleDelayMs
}

function Invoke-CurlJsonRequest {
    param(
        [string]$Method,
        [string]$Url,
        [object]$Body,
        [string]$BearerToken,
        [string]$IdempotencyKey = "",
        [string]$CorrelationId = ""
    )

    $tmpBodyFile = [System.IO.Path]::GetTempFileName()
    $tmpOutFile = [System.IO.Path]::GetTempFileName()

    try {
        $args = @(
            "-k",
            "-s",
            "-o", $tmpOutFile,
            "-w", "HTTP_STATUS:%{http_code}",
            "-X", $Method,
            "-H", "Authorization: Bearer $BearerToken",
            "-H", "Accept: application/json"
        )

        if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) {
            $args += "-H"
            $args += "Idempotency-Key: $IdempotencyKey"
        }

        if (-not [string]::IsNullOrWhiteSpace($CorrelationId)) {
            $args += "-H"
            $args += "X-Correlation-Id: $CorrelationId"
        }

        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 10 -Compress
            [System.IO.File]::WriteAllText($tmpBodyFile, $json, [System.Text.Encoding]::UTF8)

            $args += "-H"
            $args += "Content-Type: application/json"
            $args += "--data-binary"
            $args += "@$tmpBodyFile"
        }

        $args += $Url

        $result = & curl.exe @args
        $resultText = ($result | Out-String).Trim()

        $bodyText = ""
        if (Test-Path $tmpOutFile) {
            $bodyText = [System.IO.File]::ReadAllText($tmpOutFile)
        }

        $statusCode = -1
        if ($resultText -match "HTTP_STATUS:(\d{3})") {
            $statusCode = [int]$matches[1]
        }

        return [pscustomobject]@{
            StatusCode = $statusCode
            IsSuccess  = ($statusCode -ge 200 -and $statusCode -lt 300)
            Body       = $bodyText
            RawCurl    = $resultText
        }
    }
    finally {
        Remove-Item $tmpBodyFile -ErrorAction SilentlyContinue -Force
        Remove-Item $tmpOutFile -ErrorAction SilentlyContinue -Force
    }
}

function Get-Balance {
    param(
        [string]$BaseUrl,
        [string]$AccountId
    )

    $url = "$BaseUrl/api/app/banking/account-summary/$AccountId"
    $res = Invoke-CurlJsonRequest -Method "GET" -Url $url -Body $null -BearerToken $BearerToken

    if (-not $res.IsSuccess) {
        throw "Balance fetch failed. Status=$($res.StatusCode) Body=$($res.Body) Curl=$($res.RawCurl)"
    }

    $obj = $res.Body | ConvertFrom-Json
    return [decimal]$obj.balance
}

function Start-ParallelPair {
    param(
        [scriptblock]$Action1,
        [scriptblock]$Action2
    )

    Write-Log "Sequential request pair started. PowerShell 5.1 Task.Run runspace issue avoided."

    $r1 = & $Action1
    $r2 = & $Action2

    return [pscustomobject]@{
        Result1 = $r1
        Result2 = $r2
    }
}

function Normalize-TaskResult {
    param(
        [object]$Value,
        [string]$Name
    )

    if ($null -eq $Value) {
        return [pscustomobject]@{
            StatusCode = -1
            IsSuccess  = $false
            Body       = "${Name} is null"
            RawCurl    = "${Name}_NULL"
        }
    }

    if ($Value.PSObject.Properties["StatusCode"]) {
        return $Value
    }

    return [pscustomobject]@{
        StatusCode = -1
        IsSuccess  = $false
        Body       = "${Name} unexpected type: $($Value.GetType().FullName) value: $Value"
        RawCurl    = "${Name}_UNEXPECTED"
    }
}

function Invoke-Scenario1 {
    $scenarioName = "Scenario1"
    Write-Log "----- $scenarioName START -----"

    $beforeA = Get-Balance -BaseUrl $BaseUrl1 -AccountId $AccountA
    $beforeB = Get-Balance -BaseUrl $BaseUrl1 -AccountId $AccountB
    Write-Log "$scenarioName before A=$beforeA B=$beforeB"

    $key1 = [guid]::NewGuid().ToString()
    $key2 = [guid]::NewGuid().ToString()
    $corr1 = [guid]::NewGuid().ToString()
    $corr2 = [guid]::NewGuid().ToString()

    $results = Start-ParallelPair `
        -Action1 {
            Invoke-CurlJsonRequest -Method "POST" -Url "$BaseUrl1/api/app/banking/transfer" -Body @{
                fromAccountId = $AccountA
                toAccountId   = $AccountB
                amount        = $Scenario1AmountAB
                description   = "scenario1 A to B"
            } -BearerToken $BearerToken -IdempotencyKey $key1 -CorrelationId $corr1
        } `
        -Action2 {
            Invoke-CurlJsonRequest -Method "POST" -Url "$BaseUrl2/api/app/banking/transfer" -Body @{
                fromAccountId = $AccountB
                toAccountId   = $AccountA
                amount        = $Scenario1AmountBA
                description   = "scenario1 B to A"
            } -BearerToken $BearerToken -IdempotencyKey $key2 -CorrelationId $corr2
        }

    $result1 = Normalize-TaskResult -Value $results.Result1 -Name "Result1"
    $result2 = Normalize-TaskResult -Value $results.Result2 -Name "Result2"

    Write-Log "$scenarioName req1 status=$($result1.StatusCode) success=$($result1.IsSuccess) corr=$corr1 key=$key1 body=$($result1.Body)"
    Write-Log "$scenarioName req2 status=$($result2.StatusCode) success=$($result2.IsSuccess) corr=$corr2 key=$key2 body=$($result2.Body)"

    Wait-For-CacheSettle -ScenarioName $scenarioName

    $afterA = Get-Balance -BaseUrl $BaseUrl1 -AccountId $AccountA
    $afterB = Get-Balance -BaseUrl $BaseUrl1 -AccountId $AccountB
    Write-Log "$scenarioName after A=$afterA B=$afterB"

    $expectedA = $beforeA - $Scenario1AmountAB + $Scenario1AmountBA
    $expectedB = $beforeB + $Scenario1AmountAB - $Scenario1AmountBA

    $balanceOk = ($afterA -eq $expectedA) -and ($afterB -eq $expectedB)
    $sumOk = (($beforeA + $beforeB) -eq ($afterA + $afterB))
    $nonNegativeOk = ($afterA -ge 0) -and ($afterB -ge 0)

    Write-Log "$scenarioName check ExpectedA=$expectedA ExpectedB=$expectedB BalanceOk=$balanceOk SumOk=$sumOk NonNegativeOk=$nonNegativeOk"

    if ($balanceOk -and $sumOk -and $nonNegativeOk) {
        Write-Log "$scenarioName PASS"
    }
    else {
        Write-Log "$scenarioName FAIL" "ERROR"
    }
}

function Invoke-Scenario2 {
    $scenarioName = "Scenario2"
    Write-Log "----- $scenarioName START -----"

    $before = Get-Balance -BaseUrl $BaseUrl1 -AccountId $AccountA
    Write-Log "$scenarioName before A=$before"

    $results = Start-ParallelPair `
        -Action1 {
            Invoke-CurlJsonRequest -Method "POST" -Url "$BaseUrl1/api/app/banking/withdraw" -Body @{
                accountId   = $AccountA
                amount      = $Scenario2WithdrawAmount
                description = "scenario2 withdraw via instance1"
            } -BearerToken $BearerToken -IdempotencyKey ([guid]::NewGuid().ToString()) -CorrelationId ([guid]::NewGuid().ToString())
        } `
        -Action2 {
            Invoke-CurlJsonRequest -Method "POST" -Url "$BaseUrl2/api/app/banking/withdraw" -Body @{
                accountId   = $AccountA
                amount      = $Scenario2WithdrawAmount
                description = "scenario2 withdraw via instance2"
            } -BearerToken $BearerToken -IdempotencyKey ([guid]::NewGuid().ToString()) -CorrelationId ([guid]::NewGuid().ToString())
        }

    $result1 = Normalize-TaskResult -Value $results.Result1 -Name "Result1"
    $result2 = Normalize-TaskResult -Value $results.Result2 -Name "Result2"

    Write-Log "$scenarioName req1 status=$($result1.StatusCode) success=$($result1.IsSuccess) body=$($result1.Body)"
    Write-Log "$scenarioName req2 status=$($result2.StatusCode) success=$($result2.IsSuccess) body=$($result2.Body)"

    Wait-For-CacheSettle -ScenarioName $scenarioName

    $after = Get-Balance -BaseUrl $BaseUrl1 -AccountId $AccountA
    Write-Log "$scenarioName after A=$after"

    $successCount = @($result1, $result2) | Where-Object { $_.IsSuccess } | Measure-Object | Select-Object -ExpandProperty Count
    $expected = $before - ($successCount * $Scenario2WithdrawAmount)

    Write-Log "$scenarioName check SuccessCount=$successCount ExpectedA=$expected"

    if (($after -eq $expected) -and ($after -ge 0)) {
        Write-Log "$scenarioName PASS"
    }
    else {
        Write-Log "$scenarioName FAIL" "ERROR"
    }
}

New-Item -ItemType File -Path $LogPath -Force | Out-Null

switch ($Scenario) {
    "Scenario1" { Invoke-Scenario1 }
    "Scenario2" { Invoke-Scenario2 }
    "All" {
        Invoke-Scenario1
        Invoke-Scenario2
    }
}