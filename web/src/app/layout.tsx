import '@mantine/core/styles.css';
import '@mantine/notifications/styles.css';

import type { Metadata } from 'next';
import { ColorSchemeScript, MantineProvider, mantineHtmlProps, createTheme, MantineColorsTuple   } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { getLocale } from '@/i18n/server';
import { LocaleProvider } from '@/i18n/client';

const myColor: MantineColorsTuple = [
  '#E3F9E5',
  '#C1EAC5',
  '#A3D9A5',
  '#7BC47F',
  '#57AE5B',
  '#3F9142',
  '#2F8132',
  '#207227',
  '#0E5814',
  '#05400A'
];

const theme = createTheme({
  colors: {
    myColor,
  },
  primaryColor: 'myColor',
});


export const metadata: Metadata = {
  title: 'DeBoet Tools',
  description: 'Manage the category tree!',
  robots: { index: false, follow: false },
};

/**
 * The locale is resolved here rather than in the admin layout so the login screen — which
 * sits outside it — is translated too, and so `<html lang>` is right for the whole document.
 */
export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const locale = await getLocale();

  return (
    <html lang={locale} {...mantineHtmlProps}>
      <head>
        <ColorSchemeScript defaultColorScheme="auto" />
      </head>
      <body>
        <MantineProvider theme={theme}>
          <LocaleProvider locale={locale}>
            <Notifications position="top-right" />
            {children}
          </LocaleProvider>
        </MantineProvider>
      </body>
    </html>
  );
}
