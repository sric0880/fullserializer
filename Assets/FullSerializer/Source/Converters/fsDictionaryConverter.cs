using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace FullSerializer.Internal {
    // While the generic IEnumerable converter can handle dictionaries, we
    // process them separately here because we support a few more advanced
    // use-cases with dictionaries, such as inline strings. Further, dictionary
    // processing in general is a bit more advanced because a few of the
    // collection implementations are buggy.
    public class fsDictionaryConverter : fsConverter {
        public override bool CanProcess(Type type) {
            return typeof(IDictionary).IsAssignableFrom(type);
        }

        public override object CreateInstance(fsData data, Type storageType) {
            return fsMetaType.Get(Serializer.Config, storageType).CreateInstance();
        }

        public override fsResult TryDeserialize(fsData data, ref object instance_, Type storageType) {
            var instance = (IDictionary)instance_;
            var result = fsResult.Success;

            Type keyStorageType, valueStorageType;
            GetKeyValueTypes(instance.GetType(), out keyStorageType, out valueStorageType);
			string idFieldName = IDAttribute.TypeHasIDAttr(valueStorageType);

            if (data.IsList) {
                var list = data.AsList;
                for (int i = 0; i < list.Count; ++i) {
                    var item = list[i];

                    fsData keyData, valueData;
                    if ((result += CheckType(item, fsDataType.Object)).Failed) return result;
                    if ((result += CheckKey(item, "Key", out keyData)).Failed) return result;
                    if ((result += CheckKey(item, "Value", out valueData)).Failed) return result;
					if (idFieldName != null)
					{
						valueData.AsDictionary.Add(idFieldName, keyData);
					}

                    object keyInstance = null, valueInstance = null;
                    if ((result += Serializer.TryDeserialize(keyData, keyStorageType, ref keyInstance)).Failed) return result;
                    if ((result += Serializer.TryDeserialize(valueData, valueStorageType, ref valueInstance)).Failed) return result;

					if ((result += AddItemToDictionary(instance, keyInstance, valueInstance)).Failed) return result;
                }
            }
            else {
                return FailExpectedType(data, fsDataType.Array, fsDataType.Object);
            }

            return result;
        }

        public override fsResult TrySerialize(object instance_, out fsData serialized, Type storageType) {
            serialized = fsData.Null;

            var result = fsResult.Success;

            var instance = (IDictionary)instance_;

            Type keyStorageType, valueStorageType;
            GetKeyValueTypes(instance.GetType(), out keyStorageType, out valueStorageType);
			string idFieldName = IDAttribute.TypeHasIDAttr(valueStorageType);

            // No other way to iterate dictionaries and still have access to the
            // key/value info
            IDictionaryEnumerator enumerator = instance.GetEnumerator();

            var serializedKeys = new List<fsData>(instance.Count);
            var serializedValues = new List<fsData>(instance.Count);
            while (enumerator.MoveNext()) {
                fsData keyData, valueData;
                if ((result += Serializer.TrySerialize(keyStorageType, enumerator.Key, out keyData)).Failed) return result;
                if ((result += Serializer.TrySerialize(valueStorageType, enumerator.Value, out valueData)).Failed) return result;

                serializedKeys.Add(keyData);
                serializedValues.Add(valueData);
            }

            serialized = fsData.CreateList(serializedKeys.Count);
			var serializedList = serialized.AsList;

			for (int i = 0; i < serializedKeys.Count; ++i)
			{
				fsData key = serializedKeys[i];
				fsData value = serializedValues[i];

				if (idFieldName != null)
				{
					value.AsDictionary.Remove(idFieldName);
				}

				var container = new Dictionary<string, fsData>();
				container["Key"] = key;
				container["Value"] = value;
				serializedList.Add(new fsData(container));
			}

            return result;
        }

        private fsResult AddItemToDictionary(IDictionary dictionary, object key, object value) {
			if (key == null || value == null)
			{
				return fsResult.Fail("Dictionary key or value is null");
			}
			// throw an exception if the key already exists.
			if (dictionary.Contains(key)) return fsResult.Fail("The key " + key.ToString() + " already exists");
			dictionary.Add(key, value);
            return fsResult.Success;
        }

        private static void GetKeyValueTypes(Type dictionaryType, out Type keyStorageType, out Type valueStorageType) {
            // All dictionaries extend IDictionary<TKey, TValue>, so we just
            // fetch the generic arguments from it
            var interfaceType = fsReflectionUtility.GetInterface(dictionaryType, typeof(IDictionary<,>));
            if (interfaceType != null) {
                var genericArgs = interfaceType.GetGenericArguments();
                keyStorageType = genericArgs[0];
                valueStorageType = genericArgs[1];
            }
            else {
                // Fetching IDictionary<,> failed... we have to encode full type
                // information :(
                keyStorageType = typeof(object);
                valueStorageType = typeof(object);
            }
        }
    }
}