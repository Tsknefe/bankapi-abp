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

    [string]$BaseUrl1 = "https://localhost:5001",
    [string]$BaseUrl2 = "https://localhost:5002",

    [decimal]$Scenario1AmountAB = 30,
    [decimal]$Scenario1AmountBA = 20,
    [decimal]$Scenario2WithdrawAmount = 80,

    [decimal]$ExpectedStartBalanceA = 100,
    [decimal]$ExpectedStartBalanceB = 100,

    [int]$CacheSettleDelayMs = 1200,
    [string]$LogPath = ".\distributed-test-log.txt"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

function Write-Log {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )
    $line = "{0} [{1}] {2}" -f (Get-Date).ToString("o"), $Level, $Message
    $line | Tee-Object -FilePath $LogPath -Append
}

function Wait-For-CacheSettle {
    param([string]$ScenarioName)
    Write-Log "$ScenarioName waiting $CacheSettleDelayMs ms for cache settle"
    Start-Sleep -Milliseconds $CacheSettleDelayMs
}

function New-HttpClient {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.ServerCertificateCustomValidationCallback = { return $true }

    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    $client.DefaultRequestHeaders.Authorization =
        [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $BearerToken)

    return $client
}

function Invoke-JsonRequest {
    param($Client, $Method, $Url, $Body, $Headers)

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::$Method, $Url
    )

    if ($Body) {
        $json = $Body | ConvertTo-Json -Compress
        $request.Content = [System.Net.Http.StringContent]::new($json, [Text.Encoding]::UTF8, "application/json")
    }

    foreach ($key in $Headers.Keys) {
        $request.Headers.TryAddWithoutValidation($key, [string]$Headers[$key]) | Out-Null
    }

    try {
        $response = $Client.SendAsync($request).Result
        $content = $response.Content.ReadAsStringAsync().Result

        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            IsSuccess  = $response.IsSuccessStatusCode
            Body       = $content
        }
    }
    catch {
        return [pscustomobject]@{
            StatusCode = -1
            IsSuccess  = $false
            Body       = $_.Exception.Message
        }
    }
}

function Get-Balance {
    param($Client, $BaseUrl, $AccountId)

    $url = "$BaseUrl/api/app/banking/account-summary/$AccountId"
    $res = Invoke-JsonRequest $Client "Get" $url $null @{}

    if (-not $res.IsSuccess) {
        throw "Balance fetch failed"
    }

    return ([decimal](($res.Body | ConvertFrom-Json).balance))
}

function Start-ParallelPair {
    param($Action1, $Action2)

    $gate = [System.Threading.ManualResetEventSlim]::new($false)

    $t1 = [System.Threading.Tasks.Task]::Run({
        $gate.Wait()
        & $Action1
    })

    $t2 = [System.Threading.Tasks.Task]::Run({
        $gate.Wait()
        & $Action2
    })

    Start-Sleep -Milliseconds 200
    $gate.Set()

    [System.Threading.Tasks.Task]::WaitAll($t1, $t2)

    return @($t1.Result, $t2.Result)
}

# =====================
# SCENARIO 1
# =====================

function Invoke-Scenario1 {

    $scenario = "Scenario1"
    Write-Log "----- $scenario START -----"

    $c1 = New-HttpClient
    $c2 = New-HttpClient

    try {
        $beforeA = Get-Balance $c1 $BaseUrl1 $AccountA
        $beforeB = Get-Balance $c1 $BaseUrl1 $AccountB

        Write-Log "$scenario before A=$beforeA B=$beforeB"

        $key1 = [guid]::NewGuid()
        $key2 = [guid]::NewGuid()

        $corr1 = [guid]::NewGuid()
        $corr2 = [guid]::NewGuid()

        $res = Start-ParallelPair `
            { Invoke-JsonRequest $c1 "Post" "$BaseUrl1/api/app/banking/transfer" @{
                    fromAccountId=$AccountA; toAccountId=$AccountB; amount=$Scenario1AmountAB
                } @{
                    "Idempotency-Key"=$key1
                    "X-Correlation-Id"=$corr1
                }
            } `
            { Invoke-JsonRequest $c2 "Post" "$BaseUrl2/api/app/banking/transfer" @{
                    fromAccountId=$AccountB; toAccountId=$AccountA; amount=$Scenario1AmountBA
                } @{
                    "Idempotency-Key"=$key2
                    "X-Correlation-Id"=$corr2
                }
            }

        Wait-For-CacheSettle $scenario

        $afterA = Get-Balance $c1 $BaseUrl1 $AccountA
        $afterB = Get-Balance $c1 $BaseUrl1 $AccountB

        Write-Log "$scenario after A=$afterA B=$afterB"

        $sumOk = ($beforeA + $beforeB) -eq ($afterA + $afterB)
        $nonNeg = ($afterA -ge 0 -and $afterB -ge 0)

        if ($sumOk -and $nonNeg) {
            Write-Log "$scenario PASS"
        } else {
            Write-Log "$scenario FAIL" "ERROR"
        }
    }
    finally {
        $c1.Dispose()
        $c2.Dispose()
    }
}

# =====================
# SCENARIO 2
# =====================

function Invoke-Scenario2 {

    $scenario = "Scenario2"
    Write-Log "----- $scenario START -----"

    $c1 = New-HttpClient
    $c2 = New-HttpClient

    try {
        $before = Get-Balance $c1 $BaseUrl1 $AccountA
        Write-Log "$scenario before A=$before"

        $res = Start-ParallelPair `
            { Invoke-JsonRequest $c1 "Post" "$BaseUrl1/api/app/banking/withdraw" @{
                    accountId=$AccountA; amount=$Scenario2WithdrawAmount
                } @{
                    "Idempotency-Key"=[guid]::NewGuid()
                    "X-Correlation-Id"=[guid]::NewGuid()
                }
            } `
            { Invoke-JsonRequest $c2 "Post" "$BaseUrl2/api/app/banking/withdraw" @{
                    accountId=$AccountA; amount=$Scenario2WithdrawAmount
                } @{
                    "Idempotency-Key"=[guid]::NewGuid()
                    "X-Correlation-Id"=[guid]::NewGuid()
                }
            }

        Wait-For-CacheSettle $scenario

        $after = Get-Balance $c1 $BaseUrl1 $AccountA
        Write-Log "$scenario after A=$after"

        $successCount = ($res | Where-Object { $_.IsSuccess }).Count
        $expected = $before - ($successCount * $Scenario2WithdrawAmount)

        if ($after -eq $expected -and $after -ge 0) {
            Write-Log "$scenario PASS"
        } else {
            Write-Log "$scenario FAIL" "ERROR"
        }
    }
    finally {
        $c1.Dispose()
        $c2.Dispose()
    }
}

# =====================
# RUN
# =====================

New-Item -ItemType File -Path $LogPath -Force | Out-Null

switch ($Scenario) {
    "Scenario1" { Invoke-Scenario1 }
    "Scenario2" { Invoke-Scenario2 }
    "All" {
        Invoke-Scenario1
        Invoke-Scenario2
    }
}