# Host: localhost:3312  (Version 5.5.21)
# Date: 2026-11-17 10:00:00
# Generator: MySQL-Front 6.1  (Build 1.26)

/*!40101 SET NAMES utf8 */;

#
# Structure for table "tbl_ayar"
#

DROP TABLE IF EXISTS `tbl_ayar`;
CREATE TABLE `tbl_ayar` (
  `firmaid` int(11) NOT NULL DEFAULT '0',
  `firmaad` varchar(255) DEFAULT NULL,
  `firmatel` varchar(100) DEFAULT NULL,
  `firmaadres` text,
  `odemedurum` enum('Y','N','X') DEFAULT 'N',
  PRIMARY KEY (`firmaid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

#
# Data for table "tbl_ayar"
#

INSERT INTO `tbl_ayar` VALUES (1,'TEST ŞİŞ & ÇİĞ KÖFTE','02320000000','Örnek Mah. Gül Sok. No:1 Çiğli/İzmir','N');

#
# Structure for table "tbl_eleman"
#

DROP TABLE IF EXISTS `tbl_eleman`;
CREATE TABLE `tbl_eleman` (
  `elemanid` int(11) NOT NULL AUTO_INCREMENT,
  `elemanad` varchar(100) DEFAULT NULL,
  `elemannot` text,
  PRIMARY KEY (`elemanid`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8;

#
# Data for table "tbl_eleman"
#

INSERT INTO `tbl_eleman` VALUES (1,'Yönetici','Kasa açılışı ''sabah'' yapılır\\nGüle güle'),(2,'Garson Ayşe',NULL);

#
# Structure for table "tbl_odemetipi"
#

DROP TABLE IF EXISTS `tbl_odemetipi`;
CREATE TABLE `tbl_odemetipi` (
  `odemeid` int(11) NOT NULL,
  `odemekod` varchar(20) NOT NULL,
  `odemetanim` varchar(50) DEFAULT NULL,
  `odemesec` enum('Y','N') DEFAULT 'N',
  `ysodemeid` int(11) DEFAULT NULL,
  `odemesira` int(11) DEFAULT NULL,
  `odemegizle` enum('Y','N') DEFAULT 'N',
  `odemefatura` enum('Y','N') DEFAULT 'N',
  PRIMARY KEY (`odemeid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

#
# Data for table "tbl_odemetipi"
#

INSERT INTO `tbl_odemetipi` VALUES (1,'NAKIT','Nakit','N',1,2,'N','N'),(2,'KREDIKARTI','Kredi Kart','N',6,3,'N','N'),(3,'WINWIN','Win-Win','N',18,11,'N','N'),(4,'SODEXO','Sodexo Kart','N',11,6,'N','N'),(5,'MULTINET','Multinet','N',3,8,'N','N'),(6,'SETCARD','Set Kart','N',15,9,'Y','N'),(7,'SMARTICKET','Ticket Kart','N',10,4,'N','N'),(8,'METROPOL','Metropol Kart','N',24,19,'N','N'),(9,'ONLINE','Online','N',4,1,'Y','Y'),(10,'TICKETCEKI','Ticket Çeki','N',2,5,'N','N'),(11,'TOKENFLEX','Tokenflex','Y',NULL,20,'N','N');

#
# Structure for table "tbl_urunkategori"
#

DROP TABLE IF EXISTS `tbl_urunkategori`;
CREATE TABLE `tbl_urunkategori` (
  `kategoriid` int(11) NOT NULL AUTO_INCREMENT,
  `kategoriidweb` int(11) DEFAULT NULL,
  `kategoriidbinbir` varchar(50) DEFAULT NULL,
  `guncellebinbir` enum('Y','N') DEFAULT 'N',
  `kategorisira` int(11) DEFAULT NULL,
  `kategoriad` varchar(255) DEFAULT NULL,
  `kategorikdv` int(3) DEFAULT NULL,
  `kategorikisim` int(3) DEFAULT NULL,
  `paketucret` float(10,2) DEFAULT '0.00',
  `kategoriresim` mediumblob,
  `kategoriaciklama` text,
  `kategoriweb` enum('Y','N') DEFAULT 'Y',
  `kategorigoster` enum('Y','N') DEFAULT 'Y',
  `guncelle` enum('Y','N') DEFAULT 'N',
  PRIMARY KEY (`kategoriid`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8;

#
# Data for table "tbl_urunkategori"
#

INSERT INTO `tbl_urunkategori` VALUES (1,NULL,NULL,'N',1,'Eski Kategori',10,NULL,0,NULL,NULL,'Y','Y','N');

#
# Structure for table "tbl_urun"
#

DROP TABLE IF EXISTS `tbl_urun`;
CREATE TABLE `tbl_urun` (
  `urunid` int(11) NOT NULL AUTO_INCREMENT,
  `urunidweb` int(11) DEFAULT NULL,
  `guncellebinbir` enum('Y','N') DEFAULT 'N',
  `urunsira` int(11) DEFAULT NULL,
  `urunad` varchar(255) DEFAULT NULL,
  `urunad_ing` varchar(255) DEFAULT NULL,
  `urunfiyat` float(10,2) DEFAULT NULL,
  `urunindirim` float(10,2) DEFAULT NULL,
  `kampanyaadet` int(11) DEFAULT '0',
  `urunkategoriid` int(11) DEFAULT NULL,
  `urunresim` mediumblob,
  `urunaciklama` varchar(250) DEFAULT NULL,
  `urunsiparis` enum('Y','N') DEFAULT 'Y',
  `urungoster` enum('Y','N') DEFAULT 'Y',
  `guncelle` enum('Y','N') DEFAULT 'N',
  PRIMARY KEY (`urunid`)
) ENGINE=InnoDB AUTO_INCREMENT=9 DEFAULT CHARSET=utf8;

#
# Data for table "tbl_urun"
#


#
# Structure for table "tbl_urunkategoricarpan"
#

DROP TABLE IF EXISTS `tbl_urunkategoricarpan`;
CREATE TABLE `tbl_urunkategoricarpan` (
  `kategoricarpanid` int(11) NOT NULL AUTO_INCREMENT,
  `kategoricarpanidweb` int(11) DEFAULT NULL,
  `kategoriid` int(11) DEFAULT NULL,
  `kategoricarpantip` varchar(30) DEFAULT NULL,
  `kategoricarpanad` varchar(255) DEFAULT NULL,
  `kategoricarpanad_ing` varchar(255) DEFAULT NULL,
  `kategoricarpansira` smallint(6) DEFAULT NULL,
  `kategoricarpansil` enum('Y','N') DEFAULT 'N',
  `kategoricarpanyeni` enum('Y','N') DEFAULT 'N',
  PRIMARY KEY (`kategoricarpanid`)
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8;

#
# Data for table "tbl_urunkategoricarpan"
#


#
# Structure for table "tbl_uruncarpan"
#

DROP TABLE IF EXISTS `tbl_uruncarpan`;
CREATE TABLE `tbl_uruncarpan` (
  `carpanid` int(11) NOT NULL AUTO_INCREMENT,
  `carpanidweb` int(11) DEFAULT NULL,
  `kategoricarpanid` int(11) DEFAULT NULL,
  `kategoriid` int(11) DEFAULT NULL,
  `urunid` int(11) DEFAULT NULL,
  `carpantip` varchar(30) DEFAULT NULL,
  `carpanad` varchar(100) DEFAULT NULL,
  `carpanad_ing` varchar(100) DEFAULT NULL,
  `carpanfiyat` float(10,2) DEFAULT NULL,
  `carpansil` enum('Y','N') DEFAULT 'N',
  PRIMARY KEY (`carpanid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

#
# Data for table "tbl_uruncarpan"
#

