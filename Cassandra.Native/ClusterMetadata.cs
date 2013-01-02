using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using Cassandra.Native;
using System.Threading;
using System.Data.Common;
namespace Cassandra
{
/**
 * Keeps metadata on the connected cluster, including known nodes and schema definitions.
 */
    public class ClusterMetadata
    {
        internal volatile string clusterName;
        private Hosts hosts;

        internal ClusterMetadata(Hosts hosts)
        {
            this.hosts = hosts;
        }

        public Host GetHost(IPAddress address)
        {
            return hosts[address];
        }

        public Host AddHost(IPAddress address, ReconnectionPolicy rp)
        {
             hosts.AddIfNotExistsOrBringUpIfDown(address, rp);
             return hosts[address];
        }

        public void RemoveHost(IPAddress address)
        {
            hosts.RemoveIfExists(address);
        }

        public IEnumerable<IPAddress> AllHosts()
        {
            return hosts.AllEndPoints();
        }

        internal void rebuildTokenMap(string partitioner, Dictionary<IPAddress, DictSet<string>> allTokens)
        {
            this.tokenMap = TokenMap.build(partitioner, allTokens);
        }

        private volatile TokenMap tokenMap;

        public IEnumerable<IPAddress> GetReplicas(byte[] partitionKey)
        {
            if(tokenMap==null)
            {
                return new List<IPAddress>();
            }
            else
            {
                return tokenMap.GetReplicas(tokenMap.factory.hash(partitionKey));
            }
        }
    }

    internal class TokenMap
    {

        private readonly Dictionary<Token, DictSet<IPAddress>> tokenToCassandraClusterHosts;
        private readonly List<Token> ring;
        internal readonly TokenFactory factory;

        private TokenMap(TokenFactory factory, Dictionary<Token, DictSet<IPAddress>> tokenToCassandraClusterHosts, List<Token> ring)
        {
            this.factory = factory;
            this.tokenToCassandraClusterHosts = tokenToCassandraClusterHosts;
            this.ring = ring;
        }

        public static TokenMap build(String partitioner, Dictionary<IPAddress, DictSet<string>> allTokens)
        {

            TokenFactory factory = TokenFactory.getFactory(partitioner);
            if (factory == null)
                return null;

            Dictionary<Token, DictSet<IPAddress>> tokenToCassandraClusterHosts = new Dictionary<Token, DictSet<IPAddress>>();
            DictSet<Token> allSorted = new DictSet<Token>();

            foreach (var entry in allTokens)
            {
                var CassandraClusterHost = entry.Key;
                foreach (string tokenStr in entry.Value)
                {
                    try
                    {
                        Token t = factory.fromString(tokenStr);
                        allSorted.Add(t);
                        if (!tokenToCassandraClusterHosts.ContainsKey(t))
                            tokenToCassandraClusterHosts.Add(t, new DictSet<IPAddress>());
                        tokenToCassandraClusterHosts[t].Add(CassandraClusterHost);
                    }
                    catch (ArgumentException e)
                    {
                        // If we failed parsing that token, skip it
                    }
                }
            }
            return new TokenMap(factory, tokenToCassandraClusterHosts, new List<Token>(allSorted));
        }

        public DictSet<IPAddress> GetReplicas(Token token)
        {

            // Find the primary replica
            int i = ring.IndexOf(token);
            if (i < 0)
            {
                i = (i + 1) * (-1);
                if (i >= ring.Count)
                    i = 0;
            }

            return tokenToCassandraClusterHosts[ring[i]];
        }
    }


   // private readonly Dictionary<String, KeyspaceMetadata> keyspaces = new Dictionary<String, KeyspaceMetadata>();

    //private volatile TokenMap tokenMap;

    //ClusterMetadata(Cluster.Manager cluster) {
    //    this.cluster = cluster;
    //}

    //// Synchronized to make it easy to detect dropped keyspaces
    //synchronized void rebuildSchema(String keyspace, String table, ResultSet ks, ResultSet cfs, ResultSet cols) {

    //    Dictionary<String, List<Row>> cfDefs = new Dictionary<String, List<Row>>();
    //    Dictionary<String, Dictionary<String, List<Row>>> colsDefs = new Dictionary<String, Dictionary<String, List<Row>>>();

    //    // Gather cf defs
    //    for (Row row : cfs) {
    //        String ksName = row.getString(KeyspaceMetadata.KS_NAME);
    //        List<Row> l = cfDefs.get(ksName);
    //        if (l == null) {
    //            l = new ArrayList<Row>();
    //            cfDefs.put(ksName, l);
    //        }
    //        l.add(row);
    //    }

    //    // Gather columns per Cf
    //    for (Row row : cols) {
    //        String ksName = row.getString(KeyspaceMetadata.KS_NAME);
    //        String cfName = row.getString(TableMetadata.CF_NAME);
    //        Dictionary<String, List<Row>> colsByCf = colsDefs.get(ksName);
    //        if (colsByCf == null) {
    //            colsByCf = new Dictionary<String, List<Row>>();
    //            colsDefs.put(ksName, colsByCf);
    //        }
    //        List<Row> l = colsByCf.get(cfName);
    //        if (l == null) {
    //            l = new ArrayList<Row>();
    //            colsByCf.put(cfName, l);
    //        }
    //        l.add(row);
    //    }

    //    if (table == null) {
    //        assert ks != null;
    //        Set<String> addedKs = new HashSet<String>();
    //        for (Row ksRow : ks) {
    //            String ksName = ksRow.getString(KeyspaceMetadata.KS_NAME);
    //            KeyspaceMetadata ksm = KeyspaceMetadata.build(ksRow);

    //            if (cfDefs.containsKey(ksName)) {
    //                buildTableMetadata(ksm, cfDefs.get(ksName), colsDefs.get(ksName));
    //            }
    //            addedKs.add(ksName);
    //            keyspaces.put(ksName, ksm);
    //        }

    //        // If keyspace is null, it means we're rebuilding from scratch, so
    //        // remove anything that was not just added as it means it's a dropped keyspace
    //        if (keyspace == null) {
    //            Iterator<String> iter = keyspaces.keySet().iterator();
    //            while (iter.hasNext()) {
    //                if (!addedKs.contains(iter.next()))
    //                    iter.remove();
    //            }
    //        }
    //    } else {
    //        assert keyspace != null;
    //        KeyspaceMetadata ksm = keyspaces.get(keyspace);

    //        // If we update a keyspace we don't know about, something went
    //        // wrong. Log an error an schedule a full schema rebuilt.
    //        if (ksm == null) {
    //            logger.error(String.format("Asked to rebuild table %s.%s but I don't know keyspace %s", keyspace, table, keyspace));
    //            cluster.submitSchemaRefresh(null, null);
    //            return;
    //        }

    //        if (cfDefs.containsKey(keyspace))
    //            buildTableMetadata(ksm, cfDefs.get(keyspace), colsDefs.get(keyspace));
    //    }
    //}

    //private static void buildTableMetadata(KeyspaceMetadata ksm, List<Row> cfRows, Dictionary<String, List<Row>> colsDefs) {
    //    boolean hasColumns = (colsDefs != null) && !colsDefs.isEmpty();
    //    for (Row cfRow : cfRows) {
    //        String cfName = cfRow.getString(TableMetadata.CF_NAME);
    //        TableMetadata tm = TableMetadata.build(ksm, cfRow, hasColumns);

    //        if (!hasColumns || colsDefs.get(cfName) == null)
    //            continue;

    //        for (Row colRow : colsDefs.get(cfName)) {
    //            ColumnMetadata.build(tm, colRow);
    //        }
    //    }
    //}



    //CassandraClusterHost add(IPEndPoint address) {
    //    CassandraClusterHost newCassandraClusterHost = new CassandraClusterHost(address, cluster.convictionPolicyFactory);
    //    CassandraClusterHost previous = CassandraClusterHosts.putIfAbsent(address, newCassandraClusterHost);
    //    if (previous == null)
    //    {
    //        newCassandraClusterHost.getMonitor().register(cluster);
    //        return newCassandraClusterHost;
    //    }
    //    else
    //    {
    //        return null;
    //    }
    //}

    //boolean remove(CassandraClusterHost CassandraClusterHost) {
    //    return CassandraClusterHosts.remove(CassandraClusterHost.getAddress()) != null;
    //}

    //CassandraClusterHost getCassandraClusterHost(IPEndPoint address) {
    //    return CassandraClusterHosts.get(address);
    //}

    //// For internal use only
    //Collection<CassandraClusterHost> allCassandraClusterHosts() {
    //    return CassandraClusterHosts.values();
    //}

    ///**
    // * The set of CassandraClusterHosts that are replica for a given partition key.
    // * <p>
    // * Note that this method is a best effort method. Consumers should not rely
    // * too heavily on the result of this method not being stale (or even empty).
    // *
    // * @param partitionKey the partition key for which to find the set of
    // * replica.
    // * @return the (immutable) set of replicas for {@code partitionKey} as know
    // * by the driver. No strong guarantee is provided on the stalelessness of
    // * this information. It is also not guarantee that the returned set won't
    // * be empty (which is then some form of staleness).
    // */
    //public Set<CassandraClusterHost> getReplicas(ByteBuffer partitionKey) {
    //    TokenMap current = tokenMap;
    //    if (current == null) {
    //        return Collections.emptySet();
    //    } else {
    //        return current.getReplicas(current.factory.hash(partitionKey));
    //    }
    //}

    ///**
    // * The Cassandra name for the cluster connect to.
    // *
    // * @return the Cassandra name for the cluster connect to.
    // */
    //public String getClusterName() {
    //    return clusterName;
    //}

    ///**
    // * Returns the known CassandraClusterHosts of this cluster.
    // *
    // * @return A set will all the know CassandraClusterHost of this cluster.
    // */
    //public Set<CassandraClusterHost> getAllCassandraClusterHosts() {
    //    return new HashSet<CassandraClusterHost>(allCassandraClusterHosts());
    //}

    ///**
    // * Return the metadata of a keyspace given its name.
    // *
    // * @param keyspace the name of the keyspace for which metadata should be
    // * returned.
    // * @return the metadat of the requested keyspace or {@code null} if {@code
    // * keyspace} is not a known keyspace.
    // */
    //public KeyspaceMetadata getKeyspace(String keyspace) {
    //    return keyspaces.get(keyspace);
    //}

    ///**
    // * Returns a list of all the defined keyspaces.
    // *
    // * @return a list of all the defined keyspaces.
    // */
    //public List<KeyspaceMetadata> getKeyspaces() {
    //    return new ArrayList<KeyspaceMetadata>(keyspaces.values());
    //}

    ///**
    // * Return a {@code String} containing CQL queries representing the schema
    // * of this cluster.
    // *
    // * In other words, this method returns the queries that would allow to
    // * recreate the schema of this cluster.
    // *
    // * Note that the returned String is formatted to be human readable (for
    // * some defintion of human readable at least).
    // *
    // * @return the CQL queries representing this cluster schema as a {code
    // * String}.
    // */
    //public String exportSchemaAsString() {
    //    StringBuilder sb = new StringBuilder();

    //    for (KeyspaceMetadata ksm : keyspaces.values())
    //        sb.append(ksm.exportAsString()).append("\n");

    //    return sb.toString();
    //}


//}
}