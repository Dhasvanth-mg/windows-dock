#pragma once

class BatteryAlerts
{
public:
    int Observe(int ac, int flag, int percent)
    {
        if (flag == 255 || (flag & 128) || percent < 0 || percent > 100 || (ac != 0 && ac != 1))
            return 0;
        if (ac == 1 || (flag & 8))
        {
            lowest = 101;
            return 0;
        }
        constexpr int thresholds[]{5, 10, 15, 20, 25};
        for (const auto threshold : thresholds)
            if (percent <= threshold && threshold < lowest)
            {
                lowest = threshold;
                return threshold;
            }
        return 0;
    }

private:
    int lowest = 101;
};
