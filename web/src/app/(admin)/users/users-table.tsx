'use client';

import { useState, useTransition } from 'react';
import {
  ActionIcon, Badge, Button, Group, Modal, MultiSelect, PasswordInput, Stack, Table,
  TableTbody, TableTd, TableTh, TableThead, TableTr, Text, TextInput, Tooltip,
} from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { IconKey, IconLock, IconLockOpen, IconPlus, IconUserCog } from '@tabler/icons-react';
import type { ManagedUser } from './page';
import { createUser, resetPassword, setEnabled, setRoles } from './actions';

const ROLES = [
  { value: 'Admin', label: 'Admin — everything, including users' },
  { value: 'Editor', label: 'Editor — create and edit categories' },
];

export function UsersTable({ users, currentUserId }: { users: ManagedUser[]; currentUserId: string }) {
  const [creating, setCreating] = useState(false);
  const [resetting, setResetting] = useState<ManagedUser | null>(null);
  const [editingRoles, setEditingRoles] = useState<ManagedUser | null>(null);
  const [pending, startTransition] = useTransition();

  function report(result: { ok: boolean; message?: string }, success: string, onDone?: () => void) {
    if (!result.ok) {
      notifications.show({ color: 'red', title: 'Refused', message: result.message ?? 'Failed' });
      return;
    }
    notifications.show({ color: 'green', title: 'Done', message: success });
    onDone?.();
  }

  return (
    <>
      <Group>
        <Button leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)}>
          New user
        </Button>
      </Group>

      <Table striped highlightOnHover withTableBorder>
        <TableThead>
          <TableTr>
            <TableTh>Username</TableTh>
            <TableTh>Display name</TableTh>
            <TableTh w={220}>Roles</TableTh>
            <TableTh w={200}>State</TableTh>
            <TableTh w={140}>Actions</TableTh>
          </TableTr>
        </TableThead>
        <TableTbody>
          {users.map((user) => {
            const isSelf = user.id === currentUserId;

            return (
              <TableTr key={user.id}>
                <TableTd>
                  <Text size="sm" fw={500}>{user.userName}</Text>
                  <Text size="xs" c="dimmed">{user.email}</Text>
                </TableTd>
                <TableTd><Text size="sm">{user.displayName}</Text></TableTd>
                <TableTd>
                  <Group gap={4}>
                    {user.roles.length === 0 && <Text size="xs" c="dimmed">no roles</Text>}
                    {user.roles.map((role) => (
                      <Badge key={role} size="sm" color={role === 'Admin' ? 'grape' : 'blue'}>
                        {role}
                      </Badge>
                    ))}
                  </Group>
                </TableTd>
                <TableTd>
                  <Group gap={4}>
                    {isSelf && <Badge size="sm" variant="outline">you</Badge>}
                    {user.isDisabled && <Badge size="sm" color="red">disabled</Badge>}
                    {user.mustChangePassword && (
                      <Badge size="sm" color="yellow">must change password</Badge>
                    )}
                  </Group>
                </TableTd>
                <TableTd>
                  <Group gap={4} wrap="nowrap">
                    <Tooltip label="Reset password">
                      <ActionIcon variant="subtle" onClick={() => setResetting(user)}>
                        <IconKey size={16} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label="Change roles">
                      <ActionIcon variant="subtle" onClick={() => setEditingRoles(user)}>
                        <IconUserCog size={16} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label={user.isDisabled ? 'Enable' : 'Disable'}>
                      {/* Disabling yourself is refused by the API; hiding it avoids the dead end. */}
                      <ActionIcon
                        variant="subtle"
                        color={user.isDisabled ? 'green' : 'red'}
                        disabled={isSelf && !user.isDisabled}
                        onClick={() =>
                          startTransition(async () =>
                            report(
                              await setEnabled(user.id, user.isDisabled),
                              user.isDisabled ? 'Account enabled.' : 'Account disabled.',
                            ),
                          )
                        }
                      >
                        {user.isDisabled ? <IconLockOpen size={16} /> : <IconLock size={16} />}
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                </TableTd>
              </TableTr>
            );
          })}
        </TableTbody>
      </Table>

      <CreateUserModal
        opened={creating}
        pending={pending}
        onClose={() => setCreating(false)}
        onSubmit={(values) =>
          startTransition(async () =>
            report(await createUser(values), 'User created.', () => setCreating(false)),
          )
        }
      />

      <ResetPasswordModal
        user={resetting}
        pending={pending}
        onClose={() => setResetting(null)}
        onSubmit={(password) =>
          startTransition(async () =>
            report(
              await resetPassword(resetting!.id, password),
              'Password reset. They must change it at next sign-in.',
              () => setResetting(null),
            ),
          )
        }
      />

      <RolesModal
        user={editingRoles}
        pending={pending}
        onClose={() => setEditingRoles(null)}
        onSubmit={(roles) =>
          startTransition(async () =>
            report(
              await setRoles(editingRoles!.id, roles),
              'Roles updated. Their other sessions were signed out.',
              () => setEditingRoles(null),
            ),
          )
        }
      />
    </>
  );
}

function CreateUserModal({
  opened, pending, onClose, onSubmit,
}: {
  opened: boolean;
  pending: boolean;
  onClose: () => void;
  onSubmit: (values: { userName: string; email: string; displayName: string; password: string; roles: string[] }) => void;
}) {
  const form = useForm({
    initialValues: { userName: '', email: '', displayName: '', password: '', roles: ['Editor'] },
    validate: {
      userName: (v) => (v.trim() ? null : 'Required'),
      email: (v) => (/^\S+@\S+\.\S+$/.test(v) ? null : 'Enter a valid email'),
      displayName: (v) => (v.trim() ? null : 'Required'),
      password: (v) => (v.length >= 8 ? null : 'At least 8 characters'),
    },
  });

  return (
    <Modal opened={opened} onClose={onClose} title="New user">
      <form onSubmit={form.onSubmit(onSubmit)}>
        <Stack>
          <Text size="sm" c="dimmed">
            Give them this password directly. They will be required to choose their own the
            first time they sign in.
          </Text>

          <TextInput label="Username" placeholder="editor1" {...form.getInputProps('userName')} />
          <TextInput label="Email" placeholder="editor1@example.com" {...form.getInputProps('email')} />
          <TextInput label="Display name" {...form.getInputProps('displayName')} />
          <PasswordInput
            label="Temporary password"
            description="At least 8 characters."
            {...form.getInputProps('password')}
          />
          <MultiSelect label="Roles" data={ROLES} {...form.getInputProps('roles')} />

          <Button type="submit" loading={pending}>Create user</Button>
        </Stack>
      </form>
    </Modal>
  );
}

function ResetPasswordModal({
  user, pending, onClose, onSubmit,
}: {
  user: ManagedUser | null;
  pending: boolean;
  onClose: () => void;
  onSubmit: (password: string) => void;
}) {
  const form = useForm({
    initialValues: { password: '' },
    validate: { password: (v) => (v.length >= 8 ? null : 'At least 8 characters') },
  });

  return (
    <Modal opened={!!user} onClose={onClose} title={`Reset password for ${user?.userName}`}>
      <form onSubmit={form.onSubmit((v) => onSubmit(v.password))}>
        <Stack>
          <Text size="sm" c="dimmed">
            Their existing sessions end immediately, and they must choose a new password at
            next sign-in.
          </Text>
          <PasswordInput label="New temporary password" {...form.getInputProps('password')} />
          <Button type="submit" loading={pending}>Reset password</Button>
        </Stack>
      </form>
    </Modal>
  );
}

function RolesModal({
  user, pending, onClose, onSubmit,
}: {
  user: ManagedUser | null;
  pending: boolean;
  onClose: () => void;
  onSubmit: (roles: string[]) => void;
}) {
  const [roles, setRolesValue] = useState<string[]>([]);

  return (
    <Modal
      opened={!!user}
      onClose={onClose}
      title={`Roles for ${user?.userName}`}
      // Seed from the user each time the modal opens rather than holding stale state.
      onEnterTransitionEnd={() => setRolesValue(user?.roles ?? [])}
    >
      <Stack>
        <MultiSelect label="Roles" data={ROLES} value={roles} onChange={setRolesValue} />
        <Text size="sm" c="dimmed">
          Roles live in the access token, so their other sessions are signed out to apply this
          immediately.
        </Text>
        <Button loading={pending} onClick={() => onSubmit(roles)}>Save roles</Button>
      </Stack>
    </Modal>
  );
}
