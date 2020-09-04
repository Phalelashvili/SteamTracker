# SteamTracker
##### part of [steamboost.ge](https://github.com/Phalelashvili/steamboost.ge)
collects steam user info by brute-forcing official api

 
4 core machine on DigitalOcean with ~10 api key (can get away with less) takes 2-3 days
to complete 1.1 billion user scan.
(steam actually has 780 million atm, rest is just range of dead steam ids,
keeping list of them and excluding in scan *could* speed up the process)
![screenshot](https://i.imgur.com/u9ssIFg.png)

to get it running, set connection string in `App.config` and api keys separated by newline
in `apiKeys.txt`.

#### PostgreSQL table
```sql
CREATE TABLE USERS(
    steam64id BIGINT,
    avatar VARCHAR(43) NOT NULL,
    updated INT DEFAULT 0 NOT NULL,
    personaname VARCHAR(64) NULL,
    profileurl VARCHAR(64) NULL,
    communityvisibilitystate INT NULL,
    timecreated INT NULL,
    loccountrycode VARCHAR(4) NULL,
    locstatecode VARCHAR(4) NULL,
    saved BOOL DEFAULT FALSE NOT NULL,
    PRIMARY KEY(steam64id)
);
-- don't column every field if you don't need it, they take 

CREATE INDEX avatar_index ON users(avatar);
-- create more indexes if you plan to use them
```


## Similarity Search
main work is done by .net program, however, for similarity search (if you need one),
python is necessary for ![image-match](https://github.com/EdjoLabs/image-match).

main .net program can't index avatars in ElasticSearch.
`updateAvatars.py` is supposed to index avatars with image-match, constantly fetching them
is bit painful, instead, it's dumped from PostgreSQL with
```sql
copy (select distinct avatar from users where saved = false) to '/path/avatarsToUpdate.csv';
```
