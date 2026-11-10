# WCAG sRGB contrast; no dependencies. Fails the build for inaccessible accent states.
$ErrorActionPreference = 'Stop'
$t = Get-Content "$PSScriptRoot/tokens.json" -Raw -Encoding UTF8 | ConvertFrom-Json
function Luminance($hex) {
    $l=0.0; $weights=@(0.2126,0.7152,0.0722)
    for($i=0;$i -lt 3;$i++) {
        $s=[Convert]::ToInt32($hex.Substring(1+2*$i,2),16)/255.0
        $v=if($s -le 0.04045){$s/12.92}else{[math]::Pow(($s+0.055)/1.055,2.4)}
        $l += $weights[$i]*$v
    }
    return $l
}
function Contrast($a,$b) {
    $x=Luminance $a; $y=Luminance $b
    return ([math]::Max($x,$y)+0.05)/([math]::Min($x,$y)+0.05)
}
foreach($app in $t.color.accent.PSObject.Properties) {
    foreach($theme in @('dark','light')) {
        $c=$app.Value.$theme
        foreach($state in @('base','hover','pressed')) {
            $text=Contrast $c.onAccent $c.$state
            $ui=Contrast $c.$state $t.color.neutral.$theme.bg
            Write-Host ('{0} {1} {2}: text={3:F3}, UI={4:F3}' -f $app.Name,$theme,$state,$text,$ui)
            if($text -lt 4.5 -or ($theme -eq 'dark' -and $ui -lt 3)){throw 'Accent contrast failed'}
        }
    }
}
