cf-xp-gained = +{ $xp } XP
cf-xp-level = Level { $level }

cmd-givexp-desc = Give experience to every character in a company.
cmd-givexp-help = {$command} <company> <amount>
    {$command} Alpha 50
    {$command} Alpha minor|moderate|major
    {$command} Alpha adversary <relative level -4 to +4>
    {$command} Alpha hazard <simple|complex> <relative level -4 to +4>
    Each character gains that much XP. Anyone below the company's highest level gains double. 1000 XP is the next level.
cmd-givexp-company = Company
cmd-givexp-amount = XP or award
cmd-givexp-unknown = No company named { $name }.
cmd-givexp-none = { $name } has no characters in this round.
cmd-givexp-done = { $name }: { $count } characters gained { $xp } XP. { $behind } below the party level gained double.
cmd-givexp-bad = Give a positive amount, or minor, moderate, major, adversary, or hazard.
