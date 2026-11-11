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

cf-xp-level-ready = You can reach level { $level }. Open your character sheet's Level Up tab.

cf-sheet-levelup-tab = Level Up
cf-levelup-none = This body has no levels to take.
cf-levelup-max = Level { $level }. That is the highest level for now.
cf-levelup-need-xp = { $xp } more XP to reach level { $level }.
cf-levelup-title = Level { $level }: make your picks, then accept.
cf-levelup-accept = Accept
cf-levelup-missing = { $count ->
    [one] 1 pick left
   *[other] { $count } picks left
}
cf-levelup-ready = Ready.
cf-levelup-choose = Choose...
cf-levelup-spells = New spells
cf-levelup-class-feat = Class feat
cf-levelup-ancestry-feat = Ancestry feat
cf-levelup-skill-feat = Skill feat
cf-levelup-general-feat = General feat
cf-levelup-bonus-skill-feat = Extra skill feat
cf-levelup-skill-increase = Skill increase
cf-levelup-bonus-skill-increase = Extra skill increase
cf-levelup-boosts = Ability boosts
cf-levelup-boosts-pick = Ability boosts (pick { $count })
cf-levelup-language = Language
