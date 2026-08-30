'use client';

import {
  ActionIcon, Menu, useComputedColorScheme, useMantineColorScheme,
} from '@mantine/core';
import { IconCheck, IconDeviceDesktop, IconMoon, IconSun } from '@tabler/icons-react';
import { useT } from '@/i18n/client';

/**
 * Light, dark, or whatever the machine is set to.
 *
 * The trigger shows what you are currently looking at rather than which setting is stored, so on
 * "system" it follows the machine — the menu is where the distinction is made, and a moon that
 * turns into a sun when the laptop does is the more useful thing to have in the header.
 *
 * `getInitialValueInEffect` is what keeps this hydration-safe: the choice lives in localStorage,
 * which the server cannot read, so the first client render has to agree with the server's and
 * settle a beat later.
 */
export function ColorSchemeToggle() {
  const t = useT();
  const { colorScheme, setColorScheme } = useMantineColorScheme();
  const computed = useComputedColorScheme('light', { getInitialValueInEffect: true });

  const options = [
    { value: 'light', label: t('theme.light'), icon: IconSun },
    { value: 'dark', label: t('theme.dark'), icon: IconMoon },
    { value: 'auto', label: t('theme.system'), icon: IconDeviceDesktop },
  ] as const;

  return (
    <Menu position="bottom-end" width={180} withinPortal>
      <Menu.Target>
        <ActionIcon variant="subtle" color="gray" size="lg" aria-label={t('theme.label')}>
          {computed === 'dark' ? <IconMoon size={18} /> : <IconSun size={18} />}
        </ActionIcon>
      </Menu.Target>

      <Menu.Dropdown>
        <Menu.Label>{t('theme.label')}</Menu.Label>
        {options.map(({ value, label, icon: Icon }) => (
          <Menu.Item
            key={value}
            leftSection={<Icon size={16} />}
            rightSection={colorScheme === value ? <IconCheck size={14} /> : null}
            onClick={() => setColorScheme(value)}
          >
            {label}
          </Menu.Item>
        ))}
      </Menu.Dropdown>
    </Menu>
  );
}
