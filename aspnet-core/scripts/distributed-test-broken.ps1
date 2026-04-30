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
    [string]$BaseUrl2 = "https://localhost:44390",

    [decimal]$Scenario1AmountAB = 30,
    [decimal]$Scenario1AmountBA = 20,
    [decimal]$Scenario2WithdrawAmount = 80,

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

function Invoke-JsonRequest {
    param(
        [string]$Method,
        [string]$Url,
        [object]$Body,
        [hashtable]$Headers
    )

    try {
        $params = @{
            Uri         = $Url
            Method      = $Method
            Headers     = $Headers
            ContentType = "application/json"
            TimeoutSec  = 60
            ErrorAction = "Stop"
        }

        if ($null -ne $Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
        }

        $response = Invoke-WebRequest @params

        [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            IsSuccess  = $true
            Body       = $response.Content
        }
    }
    catch {
        $statusCode = -1
        $bodyText = $_.Exception.Message

        if ($_.Exception.Response) {
            try { $statusCode = [int]$_.Exception.Response.StatusCode } catch {}
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $bodyText = $reader.ReadToEnd()
                $reader.Dispose()
            }
            catch {}
        }

        [pscustomobject]@{
            StatusCode = $statusCode
            IsSuccess  = $false
            Body       = $bodyText
        }
    }
}

function Get-Balance {
    param(
        [string]$BaseUrl,
        [string]$AccountId
    )

    $res = Invoke-JsonRequest -Method "Get" -Url "$BaseUrl/api/app/banking/account-summary/$AccountId" -Body $null -Headers @{
        "Authorization" = "Bearer $BearerToken"
    }

    if (-not $res.IsSuccess) {
        throw "Balance fetch failed. Status=$($res.StatusCode) Body=$($res.Body)"
    }

    return [decimal](($res.Body | ConvertFrom-Json).balance)
}

function Start-ParallelPair {
    param(
        [scriptblock]$Action1,
        [scriptblock]$Action2
    )

    $gate = [System.Threading.ManualResetEventSlim]::new($false)

    $task1 = [System.Threading.Tasks.Task[object]]::Run([Func[object]]{
        $gate.Wait()
        return (& $Action1)
    })

    $task2 = [System.Threading.Tasks.Task[object]]::Run([Func[object]]{
        $gate.Wait()
        return (& $Action2)
    })

    Start-Sleep -Milliseconds 200
    $gate.Set()

    [System.Threading.Tasks.Task]::WaitAll(@($task1, $task2))
    @($task1.Result, $task2.Result)
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
            Invoke-JsonRequest -Method "Post" -Url "$BaseUrl1/api/app/banking/transfer" -Body @{
                fromAccountId = $AccountA
                toAccountId   = $AccountB
                amount        = $Scenario1AmountAB
                description   = "scenario1 A to B"
            } -Headers @{
                "Authorization"    = "Bearer $BearerToken"
                "Idempotency-Key"  = $key1
                "X-Correlation-Id" = $corr1
            }
        } `
        -Action2 {
            Invoke-JsonRequest -Method "Post" -Url "$BaseUrl2/api/app/banking/transfer" -Body @{
                fromAccountId = $AccountB
                toAccountId   = $AccountA
                amount        = $Scenario1AmountBA
                description   = "scenario1 B to A"
            } -Headers @{
                "Authorization"    = "Bearer $BearerToken"
                "Idempotency-Key"  = $key2
                "X-Correlation-Id" = $corr2
            }
        }

    Write-Log "$scenarioName req1 status=$($results[0].StatusCode) success=$($results[0].IsSuccess) corr=$corr1 key=$key1 body=$($results[0].Body)"
    Write-Log "$scenarioName req2 status=$($results[1].StatusCode) success=$($results[1].IsSuccess) corr=$corr2 key=$key2 body=$($results[1].Body)"

    Wait-For-CacheSettle -ScenarioName $scenarioName

    $afterA = Get-Balance -BaseUrl $BaseUrl1 -AccountId $AccountA
    $afterB = Get-Balance -BaseUrl $BaseUrl1 -AccountId $AccountB
    Write-Log "$scenarioName after A=$afterA B=$afterB"

    $sumOk = (($beforeA + $beforeB) -eq ($afterA + $afterB))
    $nonNegativeOk = ($afterA -ge 0) -and ($afterB -ge 0)

    if ($sumOk -and $nonNegativeOk) {
        Write-Log "$scenarioName PASS"
    } else {
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
            Invoke-JsonRequest -Method "Post" -Url "$BaseUrl1/api/app/banking/withdraw" -Body @{
                accountId   = $AccountA
                amount      = $Scenario2WithdrawAmount
                description = "scenario2 withdraw via instance1"
            } -Headers @{
                "Authorization"    = "Bearer $BearerToken"
                "Idempotency-Key"  = [guid]::NewGuid().ToString()
                "X-Correlation-Id" = [guid]::NewGuid().ToString()
            }
        } `
        -Action2 {
            Invoke-JsonRequest -Method "Post" -Url "$BaseUrl2/api/app/banking/withdraw" -Body @{
                accountId   = $AccountA
                amount      = $Scenario2WithdrawAmount
                description = "scenario2 withdraw via instance2"
            } -Headers @{
                "Authorization"    = "Bearer $BearerToken"
                "Idempotency-Key"  = [guid]::NewGuid().ToString()
                "X-Correlation-Id" = [guid]::NewGuid().ToString()
            }
        }

    Write-Log "$scenarioName req1 status=$($results[0].StatusCode) success=$($results[0].IsSuccess) body=$($results[0].Body)"
    Write-Log "$scenarioName req2 status=$($results[1].StatusCode) success=$($results[1].IsSuccess) body=$($results[1].Body)"

    Wait-For-CacheSettle -ScenarioName $scenarioName

    $after = Get-Balance -BaseUrl $BaseUrl1 -AccountId $AccountA
    Write-Log "$scenarioName after A=$after"

    $successCount = (@($results) | Where-Object { $_.IsSuccess }).Count
    $expected = $before - ($successCount * $Scenario2WithdrawAmount)

    if (($after -eq $expected) -and ($after -ge 0)) {
        Write-Log "$scenarioName PASS"
    } else {
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