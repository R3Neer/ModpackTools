param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-cache-read-'+[guid]::NewGuid().ToString('N'))
$clock='2026-10-13T12:00:00.0000000Z'
$oldConfig=[Environment]::GetEnvironmentVariable('MODPACKTOOLS_CONFIG_HOME')
try {
 [void][IO.Directory]::CreateDirectory($fixture)
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 &$module {param($clock)$body=(Get-Command Test-MpCacheTimestamp).Definition;Set-Item Function:script:Test-MpCacheTimestamp ([scriptblock]::Create($body.Replace('[datetimeoffset]::UtcNow',"([datetimeoffset]::Parse('$clock'))")))} $clock
 $row='{"Index":1,"Kind":"mod","Selector":"2026-10-13T00:00:00Z","Unknown":{"x":1.0,"date":"2026-10-13T12:00:00+03:00"}}'
 $normal='{"SchemaVersion":1,"CreatedUtc":"2026-10-13T12:00:00Z","ProjectId":"fixture","Results":['+$row+']}'
 $large='100000000000000000000000000000001'
 $cases=@(
  @{label='normal/date/unknown';text=$normal},
  @{label='minimal';text=$normal.Replace($row,'{"Index":1,"Kind":"mod"}')},
  @{label='schema-rounding';text=$normal.Replace('"SchemaVersion":1','"SchemaVersion":0.6')},
  @{label='schema-signed-hex';text=$normal.Replace('"SchemaVersion":1','"SchemaVersion":"-0xFFFFFFFF"')},
  @{label='index-signed-binary';text=$normal.Replace('"Index":1','"Index":"-0b11111111111111111111111111111111"')},
  @{label='index-wide-signed-hex';text=$normal.Replace('"Index":1','"Index":"-0xFFFFFFFFFFFFFFFF"')},
  @{label='index-negated-min-hex';text=$normal.Replace('"Index":1','"Index":"-0x80000000"')},
  @{label='schema-negative-padded-hex';text=$normal.Replace('"SchemaVersion":1','"SchemaVersion":"-0x00000000FFFFFFFF"')},
  @{label='schema-positive-padded-hex';text=$normal.Replace('"SchemaVersion":1','"SchemaVersion":"+0x0FFFFFFFF"')},
  @{label='index-unsigned-padded-hex';text=$normal.Replace('"Index":1','"Index":"0x000000000FFFFFFFF"')},
  @{label='index-negative-padded-min';text=$normal.Replace('"Index":1','"Index":"-0x080000000"')},
  @{label='index-negative-padded-binary';text=$normal.Replace('"Index":1',('"Index":"-0b0'+('1'*32)+'"'))},
  @{label='index-unsigned-padded-binary';text=$normal.Replace('"Index":1',('"Index":"0b0'+('1'*32)+'"'))},
  @{label='index-negative-beyond-long';text=$normal.Replace('"Index":1','"Index":"-0x000000000FFFFFFFFFFFFFFFF"')},
  @{label='index-unsigned-beyond-long';text=$normal.Replace('"Index":1','"Index":"0x000000000FFFFFFFFFFFFFFFF"')},
  @{label='index-positive-wide-low';text=$normal.Replace('"Index":1','"Index":"+0x000000000000000000001"')},
  @{label='index-negative-wide-low';text=$normal.Replace('"Index":1','"Index":"-0x000000000000000000001"')},
  @{label='huge-project';text=$normal.Replace('"fixture"',$large);project=$large},
  @{label='float-index';text=$normal.Replace('"Index":1','"Index":1.0')},
  @{label='one-array-row';text=$normal.Replace('['+$row+']','[['+$row+']]')},
  @{label='two-array-row';text=$normal.Replace('['+$row+']','[[['+$row+']]]')},
  @{label='three-root-wrappers';text='[[['+$normal+']]]'},
  @{label='future';text=$normal.Replace('2026-10-13T12:00:00Z','2030-01-01T00:00:00Z')},
  @{label='exact24h';text=$normal.Replace('2026-10-13T12:00:00Z','2026-10-12T12:00:00Z')},
  @{label='expired/bad-index';text=$normal.Replace('2026-10-13T12:00:00Z','2026-10-12T11:59:59.9999999Z').Replace('"Index":1','"Index":"bad"')},
  @{label='project/bad-index';text=$normal.Replace('"fixture"','"other"').Replace('"Index":1','"Index":"bad"')},
  @{label='later-bad-index';text=$normal.Replace('['+$row+']','['+$row+',{"Index":"bad"}]')},
  @{label='overflow/empty';text=$normal.Replace('['+$row+']','[]');selector='2147483648'},
  @{label='nonnumeric/no-read';text='{invalid';selector='01'},
  @{label='kind/source-priority';text=$normal.Replace('"Kind":"mod"','"Kind":"shaderpack"');kinds=@('mod');require=$true},
  @{label='dialect';text="{SchemaVersion:.6,CreatedUtc:'2026-10-13T12:00:00Z',ProjectId:'fixture',Results:[{Index:0x1,Kind:'mod',octal:010,extra:undefined,name:NaN,},],}"},
  @{label='unclosed-single-quote';text="'"},
  @{label='unclosed-escaped-single-quote';text="'\'"}
  @{label='raw-controls';text=$normal.Replace('"Kind":"mod"',"`"Kind`":`"mod`",`"controls`":`"line`n`ttab`rreturn`"")},
  @{label='apostrophe-escape';text=$normal.Replace('"Kind":"mod"','"Kind":"mod","apostrophe":"a\''b"')},
  @{label='numeric-dialect';text=$normal.Replace('"Kind":"mod"','"Kind":"mod","a":1.e2,"b":-010,"c":010.0,"d":0001e2')}
  @{label='negative-numeric-dialect';text=$normal.Replace('"Kind":"mod"','"Kind":"mod","a":1.e2,"b":-010,"c":-00.5')}
  @{label='comment-only';text='/* no cache */'},
  @{label='date-constructor';text=$normal.Replace('"2026-10-13T00:00:00Z"','new Date(0)')},
  @{label='date-constructor-seven';text=$normal.Replace('"2026-10-13T00:00:00Z"','new Date(2026,9,13,12,1,2,3)')},
  @{label='date-constructor-eight';text=$normal.Replace('"2026-10-13T00:00:00Z"','new Date(0,0,0,0,0,0,0,0)')},
  @{label='date-constructor-bigint';text=$normal.Replace('"2026-10-13T00:00:00Z"',('new Date('+$large+')'))},
  @{label='unknown-constructor';text=$normal.Replace('"Kind":"mod"','"Kind":"mod","Name":new Foo(1,2)')},
  @{label='unknown-last-duplicate';text=$normal.Replace('"Kind":"mod"','"Kind":"mod","Name":"old","Name":new Foo(1,2)')},
  @{label='restored-duplicate';text=$normal.Replace('"Kind":"mod"','"Kind":"mod","Name":"old","Other":"x","Name":new Foo(1,2),"Name":"restored"')},
  @{label='integer380';text=$normal.Replace('"Kind":"mod"',('"Kind":"mod","number":'+('9'*380)))},
  @{label='integer381';text=$normal.Replace('"Kind":"mod"',('"Kind":"mod","number":'+('9'*381)))},
  @{label='depth-message-in-field';text='{"MaxDepth":notJSON}'}
 )
 $calls=0
 foreach($value in @('0x1L','-0x1L','+0x1L','0b1L','+0b01u','0xFFFFFFFFL','0xFFFFFFFFu','-0xFFFFFFFFu',
  '0xFFFFFFFFul','0xFFFFFFFFn','-0xFFFFFFFFn','0x1s','0x1y','0x1d','0x1kb','0x1mb','0x1gb','0x1tb','0x1pb',
  '0x1Lkb','0xFFy','0xFFuy','0xFFFFs','0xFFFFus','0b11111111y','0b1111111111111111s','1L','1d','1kb','1n',
  '1.5L','2.5L','1.5kb','0x1_0','0x1f','0x','0b','#ff','&hff','1,234','0x--1','-0b++1','0x 1',
  '+0x','-0x','0xL','+0xL','-0xL','0xY','0xKB','+0xKB','-0xKB','0bL','+0b','-0b')){
  $cases+=@{label=('schema-text-'+$value);text=$normal.Replace('"SchemaVersion":1',('"SchemaVersion":"'+$value+'"'))}
 }
 foreach($case in $cases){
  $path=Join-Path $fixture 'last-inventory.json'
  [IO.File]::WriteAllText($path,$case.text,[Text.UTF8Encoding]::new($false))
  $before=(Get-FileHash -LiteralPath $path).Hash
  $env:MODPACKTOOLS_CONFIG_HOME=$fixture
  $selector=if($case.ContainsKey('selector')){$case.selector}else{'1'}
  $project=if($case.ContainsKey('project')){$case.project}else{'fixture'}
  $tokens=@('--inspect-resolve-reference',$fixture,$selector,$project,'--now-utc',$clock)
  $parameters=@{Selector=$selector;Project=[pscustomobject]@{Id=$project}}
  if($case.ContainsKey('kinds')){$parameters.AllowedKinds=$case.kinds;$tokens+=@('--allowed-kinds',(ConvertTo-Json -InputObject $case.kinds -Compress))}
  if($case.require){$parameters.RequirePackwiz=$true;$tokens+=@('--require-packwiz','true')}
  foreach($stage in @('read','resolve')){
   $legacyError=$null
   try{$expected=if($stage-eq'read'){&$module {Read-ModpackInventoryReferenceCache}}else{&$module {param($parameters)Resolve-ModpackInventoryNumber @parameters} $parameters};$expectedJson=ConvertTo-Json -InputObject $expected -Depth 100 -Compress}
   catch{$legacyError=$_}
   $arguments=if($stage-eq'read'){@('--inspect-read-reference-cache',$fixture)}else{$tokens}
   $native=Invoke-NativeTestProcess $Executable $arguments
   $calls++
   if($native.Stderr){throw "$($case.label)/$stage stderr: $($native.Stderr)"}
   if($legacyError){
    $error=$native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String
    $id=$legacyError.Exception.Data['ModpackTools.ErrorId']
    if(!$id){$id='ModpackTools.Inventory.InvalidReferenceCache';if($error.error.target-cne$path-or!$error.error.details){throw "$($case.label)/$stage missing E15 path/details"}}
    elseif($case.label.StartsWith('schema-text-') -and $stage -eq 'resolve' -and $null -ne $error.error.target){throw "$($case.label)/$stage wrong cast failure phase"}
    if($native.Exit-ne1-or$error.error.id-cne$id){throw "$($case.label)/$stage wrong error: $($native.Stdout)"}
   }else{
    $actual=ConvertTo-Json -InputObject ($native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String -Depth 100) -Depth 100 -Compress
    $expectedCanonical=ConvertTo-Json -InputObject ($expectedJson|ConvertFrom-Json -AsHashtable -DateKind String -Depth 100) -Depth 100 -Compress
    if($native.Exit-ne0-or$actual-cne$expectedCanonical){throw "$($case.label)/$stage differs: expected $expectedCanonical; actual $actual"}
   }
  }
  if((Get-FileHash -LiteralPath $path).Hash-cne$before){throw 'Reader changed its cache'}
  $exclusive=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$exclusive.Dispose()
 }
 foreach($value in @('"2026-10-13T00:00:00Z"',$large,'1.234567891234567','["a",null,"b"]',
  '"2026-10-13T00:00:00 "','"2026-10-13T00:00:00Z "','"2026-10-13T00:00:00.12345678Z"',
  '"2026-10-13T24:00:00Z"','"2026-10-13T00:00:00+99:99"','"2026-10-13T00:00:00+02:"',
  '"2026-10-13T00:00:00:"','"2026-10-13T00:00:0000"',
  '"/Date(0)/"','"/Date(0+99)/"','"/Date(9223372036854775807)/"')){
  [IO.File]::WriteAllText($path,$normal.Replace('"2026-10-13T00:00:00Z"',$value))
  $expected=&$module {param($project)$item=Resolve-ModpackInventoryNumber -Selector '1' -Project ([pscustomobject]@{Id=$project});[string]$item.Selector} 'fixture'
  $native=Invoke-NativeTestProcess $Executable @('--inspect-resolve-reference',$fixture,'1','fixture','--now-utc',$clock,'--string-field','Selector')
  if($native.Exit-ne0-or$native.Stderr-or($native.Stdout|ConvertFrom-Json -DateKind String)-cne$expected){throw 'Selected reference consumer string projection changed'}
  $calls++
 }
 "Native cache read/resolve: $calls full legacy and consumer comparisons, integer precision/float type/date data, boundary priorities, E15, no writes and handle release passed"
} finally {
 [Environment]::SetEnvironmentVariable('MODPACKTOOLS_CONFIG_HOME',$oldConfig)
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 if(Test-Path -LiteralPath $fixture){
  $resolved=[IO.Path]::GetFullPath($fixture)
  if(!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-cache-read-*'){throw 'Unsafe fixture cleanup'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
 }
}
