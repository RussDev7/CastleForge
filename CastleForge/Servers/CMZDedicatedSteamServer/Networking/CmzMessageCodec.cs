/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.IO;
using System;

namespace CMZDedicatedLidgrenServer
{
    /// <summary>
    /// Builds normal CastleMiner Z / DNA.Net message payloads:
    /// [msgId][SendData body][xor checksum]
    ///
    /// This does NOT send packets by itself.
    /// Your server still wraps the returned payload into channel-0 or channel-1 transport packets.
    /// </summary>
    public sealed class CmzMessageCodec
    {
        #region Fields

        private readonly Assembly _gameAsm;
        private readonly Assembly _commonAsm;
        private readonly Action<string> _log;

        private readonly Dictionary<byte, string> _messageIdToType = [];
#pragma warning disable IDE0090
        private readonly Dictionary<string, byte> _typeToMessageId = new Dictionary<string, byte>(StringComparer.Ordinal);
#pragma warning restore IDE0090

        #endregion

        #region Init

        public CmzMessageCodec(Assembly gameAsm, Assembly commonAsm, Action<string> log = null)
        {
            _gameAsm = gameAsm ?? throw new ArgumentNullException(nameof(gameAsm));
            _commonAsm = commonAsm ?? throw new ArgumentNullException(nameof(commonAsm));
            _log = log ?? (_ => { });

            BuildMessageMaps();
        }

        private void BuildMessageMaps()
        {
            var messageType = ResolveType("DNA.Net.Message") ?? throw new InvalidOperationException("DNA.Net.Message type not found.");
            try
            {
                RuntimeHelpers.RunClassConstructor(messageType.TypeHandle);
            }
            catch
            {
            }

            var typesField = messageType.GetField("_messageTypes", BindingFlags.NonPublic | BindingFlags.Static);
            var idsField = messageType.GetField("_messageIDs", BindingFlags.NonPublic | BindingFlags.Static);

            if (typesField?.GetValue(null) is not Type[] types || idsField?.GetValue(null) is not IDictionary ids)
                throw new InvalidOperationException("DNA.Net.Message message registry not available.");

            for (byte i = 0; i < types.Length; i++)
            {
                var t = types[i];
                if (t == null)
                    continue;

                _messageIdToType[i] = t.FullName;
                _typeToMessageId[t.FullName] = i;
            }

            _log?.Invoke($"CmzMessageCodec: Loaded {_messageIdToType.Count} message types.");
        }
        #endregion

        #region Public API

        public bool HasMessageType(string fullTypeName)
        {
            return !string.IsNullOrWhiteSpace(fullTypeName) && _typeToMessageId.ContainsKey(fullTypeName);
        }

        public byte GetMessageId(string fullTypeName)
        {
            return !_typeToMessageId.TryGetValue(fullTypeName, out byte msgId)
                ? throw new KeyNotFoundException("Unknown message type: " + fullTypeName)
                : msgId;
        }

        public Type GetMessageClrType(string fullTypeName)
        {
            return ResolveType(fullTypeName);
        }

        /// <summary>
        /// Gets the reflected full type name for a registered CMZ / DNA.Net message id.
        ///
        /// Purpose:
        /// - Allows callers to identify an incoming payload by its message id.
        /// - Provides the reverse lookup for _messageIdToType.
        ///
        /// Notes:
        /// - Returns null if the message id is unknown.
        /// </summary>
        public string GetTypeName(byte messageId)
        {
            return _messageIdToType.TryGetValue(messageId, out var typeName)
                ? typeName
                : null;
        }

        /// <summary>
        /// Builds the raw inner payload used by CMZ messages:
        /// [msgId][message body][xor checksum]
        /// </summary>
        public byte[] BuildPayload(string fullTypeName, Action<object> initMessage)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
                throw new ArgumentNullException(nameof(fullTypeName));

            if (!_typeToMessageId.TryGetValue(fullTypeName, out byte msgId))
                throw new InvalidOperationException("Unknown message type: " + fullTypeName);

            var msgType = ResolveType(fullTypeName) ?? throw new InvalidOperationException("CLR type not found: " + fullTypeName);

            object msg = CreateMessageInstance(msgType) ?? throw new InvalidOperationException("Failed to create message instance: " + fullTypeName);
            initMessage?.Invoke(msg);

            var sendData = FindInstanceMethod(msgType, "SendData", [typeof(BinaryWriter)]) ?? throw new InvalidOperationException(fullTypeName + ".SendData(BinaryWriter) not found.");
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(msgId);

            sendData.Invoke(msg, [writer]);
            writer.Flush();

            int len = (int)ms.Position;
            writer.Write(XorChecksum(ms.GetBuffer(), 0, len));
            writer.Flush();

            return ms.ToArray();
        }

        public bool TryBuildPayload(string fullTypeName, Action<object> initMessage, out byte[] payload)
        {
            payload = null;

            try
            {
                payload = BuildPayload(fullTypeName, initMessage);
                return true;
            }
            catch (Exception ex)
            {
                _log?.Invoke("CmzMessageCodec.BuildPayload failed for " + fullTypeName + ": " + ex.Message);
                return false;
            }
        }

        public void SetMember(object instance, string memberName, object value)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            if (string.IsNullOrWhiteSpace(memberName))
                throw new ArgumentNullException(nameof(memberName));

            var t = instance.GetType();

            var field = FindField(t, memberName);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }

            var prop = FindProperty(t, memberName);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(instance, value, null);
                return;
            }

            throw new MissingMemberException(t.FullName, memberName);
        }

        public void SetEnumMember(object instance, string memberName, string enumTypeName, object rawValue)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            var enumType = ResolveType(enumTypeName) ?? throw new InvalidOperationException("Enum type not found: " + enumTypeName);
            object enumValue = Enum.ToObject(enumType, rawValue);
            SetMember(instance, memberName, enumValue);
        }
        #endregion

        #region Helpers

        private object CreateMessageInstance(Type msgType)
        {
            try
            {
                return Activator.CreateInstance(msgType, true);
            }
            catch
            {
            }

            try
            {
                var ctor = msgType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                if (ctor != null)
                    return ctor.Invoke(null);
            }
            catch
            {
            }

            return null;
        }

        private Type ResolveType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            var t = _gameAsm.GetType(fullName, false);
            if (t != null)
                return t;

            t = _commonAsm.GetType(fullName, false);
            if (t != null)
                return t;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName, false);
                if (t != null)
                    return t;
            }

            return null;
        }

        private static MethodInfo FindInstanceMethod(Type type, string name, Type[] paramTypes)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                var method = t.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    paramTypes,
                    null);

                if (method != null)
                    return method;
            }

            return null;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field;
            }

            return null;
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                var prop = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                    return prop;
            }

            return null;
        }

        private static byte XorChecksum(byte[] data, int offset, int count)
        {
            byte c = 0;
            if (data == null || count <= 0)
                return 0;

            int end = offset + count;
            for (int i = offset; i < end && i < data.Length; i++)
                c ^= data[i];

            return c;
        }
        #endregion
    }
}