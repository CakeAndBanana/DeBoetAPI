'use client';

import { Alert, Button, Container, Stack } from '@mantine/core';

/**
 * Last resort for anything the data layer could not handle — chiefly a 403, which should be
 * unreachable now that non-admins are refused at login. Next hides the real message in
 * production builds, so this stays deliberately generic and offers a way out.
 */
export default function AdminError({ reset }: { error: Error; reset: () => void }) {
  return (
    <Container size="sm" pt="xl">
      <Stack>
        <Alert color="red" title="That did not work">
          The request was refused. If you were signed in for a while, try signing in again —
          otherwise this account may not have administrator access.
        </Alert>
        <Button onClick={reset} w="fit-content">Try again</Button>
      </Stack>
    </Container>
  );
}
