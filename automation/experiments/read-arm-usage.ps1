param(
    [Parameter(Mandatory)][string]$LogPath,
    [Parameter(Mandatory)][string]$ExpectedAgent
)
$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $LogPath).Path
$meta = Get-Content -LiteralPath $resolved -TotalCount 1 | ConvertFrom-Json
$spawn = $meta.payload.source.subagent.thread_spawn
if (!$spawn.agent_path -or $spawn.agent_path -cne $ExpectedAgent) {
    throw 'Rollout is not attributable to the expected subagent. Do not use a parent or another arm log.'
}
$last = $null
$lastTime = $null
$firstRequestInput = $null
$contexts = [System.Collections.Generic.HashSet[string]]::new()
foreach ($line in [System.IO.File]::ReadLines($resolved)) {
    $record = $line | ConvertFrom-Json
    if ($record.type -eq 'turn_context') {
        [void]$contexts.Add("$($record.payload.model)/$($record.payload.effort)")
    }
    if ($record.type -eq 'event_msg' -and $record.payload.type -eq 'token_count' -and $record.payload.info.total_token_usage) {
        if ($null -eq $firstRequestInput) { $firstRequestInput = $record.payload.info.last_token_usage.input_tokens }
        $last = $record.payload.info.total_token_usage
        $lastTime = $record.timestamp
    }
}
if (!$last) { throw 'No reported token usage; do not substitute zero.' }
foreach ($field in @('input_tokens', 'cached_input_tokens', 'output_tokens', 'total_tokens')) {
    if ($null -eq $last.$field -or $last.$field -lt 0) { throw "Missing or invalid $field; do not substitute zero." }
}
if ($last.cached_input_tokens -gt $last.input_tokens) { throw 'Cached input exceeds input.' }
[pscustomobject]@{
    agent = $spawn.agent_path
    thread = $meta.payload.id
    parentThread = $spawn.parent_thread_id
    log = $resolved
    counterTimestamp = $lastTime
    modelAndEffort = @($contexts | Sort-Object)
    firstRequestInput = $firstRequestInput
    input = $last.input_tokens
    cachedInput = $last.cached_input_tokens
    uncachedInput = $last.input_tokens - $last.cached_input_tokens
    output = $last.output_tokens
    reasoningOutput = $last.reasoning_output_tokens
    total = $last.total_tokens
    source = 'Last cumulative rollout counter. Controller/evaluator overhead excluded; not an isolated image-token measurement.'
} | ConvertTo-Json
