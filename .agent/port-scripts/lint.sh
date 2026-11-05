R=/tmp/claude-1000/-home-pontus-projects-CarpFinder/11f1431d-47d0-4779-9f94-ee67937f4092/scratchpad
timeout 120 dotnet run --project Content.YAMLLinter -c Tools --no-build > $R/l.txt 2>&1
echo total $(grep -cE '^::error' $R/l.txt) nonloc $(grep -E '^::error' $R/l.txt | grep -vc 'No localization')
sed -n 1,2p $R/l.txt | grep -v '^::error' | cut -c1-240
grep -E '^::error' $R/l.txt | grep -v 'No localization' | sed -E 's/^::error in \/Prototypes\/_CF\///; s/\([0-9,-]+\) */ /' | sed -E 's/Content\.[A-Za-z]+\.[A-Za-z.]*\.//; s/^\S+ //' | sed -E 's/with id (CF\w{0,18}).*/with id \1/' | cut -c1-80 | sort | uniq -c | sort -rn | head -${1:-40}
